using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Permissions.Application.DTOs;
using Permissions.Application.Services;
using Permissions.Domain.ValueObjects;
using Permissions.Infrastructure;

namespace Permissions.Integration.Tests;

public sealed partial class PermissionIntegrationTests
{
  private static GrantPermissionRequest Grant(string relation = "viewer", string? subjectRelation = null,
      DateTime? expiresAt = null) =>
      new("group", "finance", relation, "document", "tuple-lifecycle", subjectRelation, expiresAt);

  [Fact]
  public async Task RejectedFixtureConfigurationLeavesExistingTestDatabaseUntouched()
  {
    var target = DisposableTestDatabase.ValidateConfiguredTarget(DbContext.Database.GetConnectionString());
    var before = await DbContext.RelationTuples.CountAsync();
    var invalidFixture = new DatabaseFixture(target.ConnectionString, null);

    await Assert.ThrowsAsync<InvalidOperationException>(() => invalidFixture.InitializeAsync());
    await Assert.ThrowsAsync<InvalidOperationException>(() => invalidFixture.DisposeAsync());

    Assert.Equal(before, await DbContext.RelationTuples.CountAsync());
  }

  [Fact]
  public async Task SixFieldIdentitySupportsDirectAndDistinctUsersetsWithExactRevocation()
  {
    await PermissionService.GrantAsync(Grant());
    await PermissionService.GrantAsync(Grant());
    await PermissionService.GrantAsync(Grant(subjectRelation: "member"));
    await PermissionService.GrantAsync(Grant(subjectRelation: "member"));
    await PermissionService.GrantAsync(Grant(subjectRelation: "manager"));
    Assert.Equal(3, await DbContext.RelationTuples.CountAsync());

    await PermissionService.RevokeAsync(new TupleKey("document", "tuple-lifecycle", "viewer", "group", "finance"));
    Assert.Equal(2, await DbContext.RelationTuples.CountAsync());
    Assert.DoesNotContain(await DbContext.RelationTuples.AsNoTracking().ToListAsync(), t => t.SubjectRelation is null);

    await PermissionService.RevokeAsync(new TupleKey("document", "tuple-lifecycle", "viewer", "group", "finance", "member"));
    await PermissionService.RevokeAsync(new TupleKey("document", "tuple-lifecycle", "viewer", "group", "finance", "member"));
    Assert.Equal("manager", Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync()).SubjectRelation);
  }

  [Fact]
  public async Task ExpiredGrantReactivatesAndActiveExpirationCannotBeChanged()
  {
    var originalExpiry = DateTime.UtcNow.AddDays(1);
    originalExpiry = originalExpiry.AddTicks(-(originalExpiry.Ticks % 10));
    await PermissionService.GrantAsync(Grant(expiresAt: originalExpiry));
    var original = Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
    await PermissionService.GrantAsync(Grant(expiresAt: originalExpiry));
    var unchanged = Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
    Assert.Equal(original.Id, unchanged.Id);
    Assert.Equal(original.CreatedAt, unchanged.CreatedAt);
    Assert.Equal(originalExpiry, unchanged.ExpiresAt);

    await Assert.ThrowsAsync<TupleGrantConflictException>(() => PermissionService.GrantAsync(Grant()));
    await Assert.ThrowsAsync<TupleGrantConflictException>(() => PermissionService.GrantAsync(
        Grant(expiresAt: originalExpiry.AddHours(1))));
    Assert.Equal(originalExpiry, Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync()).ExpiresAt);

    await DbContext.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE relation_tuples SET expires_at = {DateTime.UtcNow.AddDays(-1)}, created_at = {DateTime.UtcNow.AddDays(-2)} WHERE id = {original.Id}");
    var expired = Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
    Assert.False((await PermissionService.CheckAsync(new CheckPermissionRequest(
        "group", "finance", "viewer", "document", "tuple-lifecycle"))).Allowed);
    await PermissionService.GrantAsync(Grant());
    var renewed = Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
    Assert.Equal(original.Id, renewed.Id);
    Assert.Null(renewed.ExpiresAt);
    Assert.True(renewed.CreatedAt > expired.CreatedAt);
    Assert.True((await PermissionService.CheckAsync(new CheckPermissionRequest(
        "group", "finance", "viewer", "document", "tuple-lifecycle"))).Allowed);
  }

  [Fact]
  public async Task RevokingExpiredTupleRemovesItAndAbsentRevokeIsIdempotent()
  {
    await PermissionService.GrantAsync(Grant());
    await DbContext.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE relation_tuples SET expires_at = {DateTime.UtcNow.AddDays(-1)}");
    var key = new TupleKey("document", "tuple-lifecycle", "viewer", "group", "finance");
    await PermissionService.RevokeAsync(key);
    await PermissionService.RevokeAsync(key);
    Assert.Empty(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ConcurrentGrantsUseUniqueIndexAndClassifyConflict(bool differentExpirations)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var expiry = DateTime.UtcNow.AddDays(1);
    expiry = expiry.AddTicks(-(expiry.Ticks % 10));
    var first = Grant(expiresAt: expiry);
    var second = Grant(expiresAt: differentExpirations ? expiry.AddHours(1) : expiry);
    await using var blocker = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await blocker.OpenAsync(timeout.Token);
    await using var transaction = await blocker.BeginTransactionAsync(timeout.Token);
    await using (var lockCommand = new NpgsqlCommand("LOCK TABLE relation_tuples IN SHARE MODE", blocker, transaction))
      await lockCommand.ExecuteNonQueryAsync(timeout.Token);

    var firstTask = TryGrantInNewScopeAsync(first, timeout.Token);
    var secondTask = TryGrantInNewScopeAsync(second, timeout.Token);
    try
    {
      await WaitForTupleLockWaitersAsync(2, timeout.Token);
    }
    finally
    {
      await transaction.CommitAsync(CancellationToken.None);
    }

    var results = await Task.WhenAll(firstTask, secondTask).WaitAsync(timeout.Token);
    Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
    if (differentExpirations)
    {
      Assert.Single(results, error => error is null);
      Assert.Single(results, error => error is TupleGrantConflictException);
      var stored = Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
      Assert.Contains(stored.ExpiresAt, new DateTime?[] { first.ExpiresAt, second.ExpiresAt });
    }
    else
      Assert.All(results, error => Assert.Null(error));
  }

  [Fact]
  public async Task ExpirationElapsedDuringDatabaseLockWaitCannotBeGranted()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await using var blocker = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await blocker.OpenAsync(timeout.Token);
    await using var transaction = await blocker.BeginTransactionAsync(timeout.Token);
    await using (var command = new NpgsqlCommand("LOCK TABLE relation_tuples IN SHARE MODE", blocker, transaction))
      await command.ExecuteNonQueryAsync(timeout.Token);

    var expiry = DateTime.UtcNow.AddSeconds(2);
    var pending = TryGrantInNewScopeAsync(Grant(expiresAt: expiry), timeout.Token);
    try
    {
      await WaitForTupleLockWaitersAsync(1, timeout.Token);
      await using var clock = new NpgsqlConnection(DbContext.Database.GetConnectionString());
      await clock.OpenAsync(timeout.Token);
      while (true)
      {
        await using var query = new NpgsqlCommand("SELECT clock_timestamp()", clock);
        if ((DateTime)(await query.ExecuteScalarAsync(timeout.Token))! >= expiry) break;
        await Task.Delay(25, timeout.Token);
      }
    }
    finally
    {
      await transaction.CommitAsync(CancellationToken.None);
    }

    Assert.IsType<ArgumentException>(await pending.WaitAsync(timeout.Token));
    Assert.Empty(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
  }

  [Fact]
  public async Task RolledBackUniqueConflictAfterExpiryRejectsGrantAndAllowsRecovery()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    await using var blocker = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await blocker.OpenAsync(timeout.Token);
    await using var transaction = await blocker.BeginTransactionAsync(timeout.Token);
    await InsertUncommittedTupleAsync(blocker, transaction, Guid.NewGuid(),
        PgPrecision(DateTime.UtcNow), null, timeout.Token);

    using var scope = _services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<PermissionService>();
    var expiry = DateTime.UtcNow.AddSeconds(5);
    var pending = service.GrantAsync(Grant(expiresAt: expiry), timeout.Token);
    try
    {
      await WaitForUniqueConflictWaitersAsync(1, timeout.Token);
      await WaitUntilDatabaseTimeAsync(expiry, timeout.Token);
    }
    finally
    {
      await transaction.RollbackAsync(CancellationToken.None);
    }

    await Assert.ThrowsAsync<ArgumentException>(() => pending.WaitAsync(timeout.Token));
    Assert.Empty(await DbContext.RelationTuples.AsNoTracking().ToListAsync());

    // The same DbContext must remain usable after the rejected transaction.
    await service.GrantAsync(Grant(expiresAt: DateTime.UtcNow.AddMinutes(5)), timeout.Token);
    Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
  }

  [Fact]
  public async Task RolledBackUniqueConflictBeforeExpiryUsesPostWaitCreationTime()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await using var blocker = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await blocker.OpenAsync(timeout.Token);
    await using var transaction = await blocker.BeginTransactionAsync(timeout.Token);
    await InsertUncommittedTupleAsync(blocker, transaction, Guid.NewGuid(),
        PgPrecision(DateTime.UtcNow), null, timeout.Token);

    var expiry = PgPrecision(DateTime.UtcNow.AddSeconds(15));
    var pending = TryGrantInNewScopeAsync(Grant(expiresAt: expiry), timeout.Token);
    DateTime beforeRelease;
    try
    {
      await WaitForUniqueConflictWaitersAsync(1, timeout.Token);
      beforeRelease = await ReadDatabaseTimeAsync(timeout.Token);
    }
    finally
    {
      await transaction.RollbackAsync(CancellationToken.None);
    }

    Assert.Null(await pending.WaitAsync(timeout.Token));
    var stored = Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
    Assert.Equal(expiry, stored.ExpiresAt);
    Assert.True(stored.CreatedAt >= beforeRelease);
    Assert.True(stored.CreatedAt < expiry);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CommittedUniqueConflictPreservesActiveGrantMetadata(bool differentExpiration)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await using var blocker = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await blocker.OpenAsync(timeout.Token);
    await using var transaction = await blocker.BeginTransactionAsync(timeout.Token);
    var id = Guid.NewGuid();
    var createdAt = PgPrecision(DateTime.UtcNow.AddHours(-2));
    var expiry = PgPrecision(DateTime.UtcNow.AddDays(1));
    await InsertUncommittedTupleAsync(blocker, transaction, id, createdAt, expiry, timeout.Token);

    var requested = differentExpiration ? expiry.AddHours(1) : expiry;
    var pending = TryGrantInNewScopeAsync(Grant(expiresAt: requested), timeout.Token);
    try
    {
      await WaitForUniqueConflictWaitersAsync(1, timeout.Token);
    }
    finally
    {
      await transaction.CommitAsync(CancellationToken.None);
    }

    var error = await pending.WaitAsync(timeout.Token);
    if (differentExpiration)
      Assert.IsType<TupleGrantConflictException>(error);
    else
      Assert.Null(error);
    var stored = Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
    Assert.Equal(id, stored.Id);
    Assert.Equal(createdAt, stored.CreatedAt);
    Assert.Equal(expiry, stored.ExpiresAt);
  }

  [Fact]
  public async Task CommittedExpiredConflictReactivatesWithPostWaitCreationTime()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await using var blocker = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await blocker.OpenAsync(timeout.Token);
    await using var transaction = await blocker.BeginTransactionAsync(timeout.Token);
    var id = Guid.NewGuid();
    await InsertUncommittedTupleAsync(blocker, transaction, id,
        PgPrecision(DateTime.UtcNow.AddDays(-2)), PgPrecision(DateTime.UtcNow.AddDays(-1)),
        timeout.Token);

    var requested = PgPrecision(DateTime.UtcNow.AddDays(1));
    var pending = TryGrantInNewScopeAsync(Grant(expiresAt: requested), timeout.Token);
    DateTime beforeRelease;
    try
    {
      await WaitForUniqueConflictWaitersAsync(1, timeout.Token);
      beforeRelease = await ReadDatabaseTimeAsync(timeout.Token);
    }
    finally
    {
      await transaction.CommitAsync(CancellationToken.None);
    }

    Assert.Null(await pending.WaitAsync(timeout.Token));
    var stored = Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
    Assert.Equal(id, stored.Id);
    Assert.Equal(requested, stored.ExpiresAt);
    Assert.True(stored.CreatedAt >= beforeRelease);
  }

  [Fact]
  public async Task CanceledUniqueConflictWaitRollsBackAndReleasesTransaction()
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    using var canceled = new CancellationTokenSource();
    await using var blocker = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await blocker.OpenAsync(timeout.Token);
    await using var transaction = await blocker.BeginTransactionAsync(timeout.Token);
    await InsertUncommittedTupleAsync(blocker, transaction, Guid.NewGuid(),
        PgPrecision(DateTime.UtcNow), null, timeout.Token);

    using var scope = _services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<PermissionService>();
    var pending = service.GrantAsync(Grant(expiresAt: DateTime.UtcNow.AddDays(1)), canceled.Token);
    try
    {
      await WaitForUniqueConflictWaitersAsync(1, timeout.Token);
      canceled.Cancel();
      var error = await Record.ExceptionAsync(() => pending.WaitAsync(timeout.Token));
      Assert.True(error is OperationCanceledException or PostgresException { SqlState: "57014" },
          $"Unexpected cancellation result: {error?.GetType().Name ?? "none"}");
    }
    finally
    {
      await transaction.RollbackAsync(CancellationToken.None);
    }

    Assert.Empty(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
    await service.GrantAsync(Grant(), timeout.Token);
    Assert.Single(await DbContext.RelationTuples.AsNoTracking().ToListAsync());
  }

  private static DateTime PgPrecision(DateTime timestamp) =>
      timestamp.AddTicks(-(timestamp.Ticks % 10));

  private static async Task InsertUncommittedTupleAsync(
      NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id,
      DateTime createdAt, DateTime? expiresAt, CancellationToken token)
  {
    await using var command = new NpgsqlCommand("""
        INSERT INTO relation_tuples (id, object_type, object_id, relation, subject_type,
          subject_id, subject_relation, created_at, expires_at)
        VALUES (@id, 'document', 'tuple-lifecycle', 'viewer', 'group', 'finance',
          NULL, @created_at, @expires_at)
        """, connection, transaction);
    command.Parameters.AddWithValue("id", id);
    command.Parameters.AddWithValue("created_at", createdAt);
    command.Parameters.Add("expires_at", NpgsqlDbType.TimestampTz).Value =
        expiresAt.HasValue ? expiresAt.Value : DBNull.Value;
    await command.ExecuteNonQueryAsync(token);
  }

  private async Task WaitForUniqueConflictWaitersAsync(int expected, CancellationToken token)
  {
    await using var monitor = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await monitor.OpenAsync(token);
    while (true)
    {
      await using var command = new NpgsqlCommand("""
          SELECT count(*) FROM pg_stat_activity
          WHERE datname = current_database() AND pid <> pg_backend_pid()
            AND wait_event_type = 'Lock' AND wait_event = 'transactionid'
            AND query LIKE '%INSERT INTO relation_tuples AS existing%'
          """, monitor);
      if ((long)(await command.ExecuteScalarAsync(token))! >= expected) return;
      await Task.Delay(25, token);
    }
  }

  private async Task WaitUntilDatabaseTimeAsync(DateTime target, CancellationToken token)
  {
    await using var connection = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await connection.OpenAsync(token);
    while (true)
    {
      await using var query = new NpgsqlCommand("SELECT clock_timestamp()", connection);
      if ((DateTime)(await query.ExecuteScalarAsync(token))! >= target) return;
      await Task.Delay(25, token);
    }
  }

  private async Task<DateTime> ReadDatabaseTimeAsync(CancellationToken token)
  {
    await using var connection = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await connection.OpenAsync(token);
    await using var query = new NpgsqlCommand("SELECT clock_timestamp()", connection);
    return (DateTime)(await query.ExecuteScalarAsync(token))!;
  }

  private async Task<Exception?> TryGrantInNewScopeAsync(GrantPermissionRequest request, CancellationToken token)
  {
    using var scope = _services.CreateScope();
    try
    {
      await scope.ServiceProvider.GetRequiredService<PermissionService>().GrantAsync(request, token);
      return null;
    }
    catch (Exception error)
    {
      return error;
    }
  }

  private async Task WaitForTupleLockWaitersAsync(int expected, CancellationToken token)
  {
    await using var monitor = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await monitor.OpenAsync(token);
    while (true)
    {
      await using var command = new NpgsqlCommand("""
          SELECT count(*) FROM pg_locks l
          JOIN pg_class c ON c.oid = l.relation
          WHERE l.locktype = 'relation' AND NOT l.granted
            AND c.relname = 'relation_tuples'
          """, monitor);
      if ((long)(await command.ExecuteScalarAsync(token))! >= expected) return;
      await Task.Delay(25, token);
    }
  }

  [Fact]
  public async Task MigrationPreservesPopulatedDataAndDowngradeFailsSafelyOnNewIdentity()
  {
    var source = DisposableTestDatabase.ValidateConfiguredTarget(DbContext.Database.GetConnectionString());
    var databaseName = "permissions_tuple_migration_disposable_" + Guid.NewGuid().ToString("N");
    var target = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = databaseName };
    DisposableTestDatabase.ValidateConfiguredTarget(target.ConnectionString);
    var admin = new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" };
    await using (var connection = new NpgsqlConnection(admin.ConnectionString))
    {
      await connection.OpenAsync();
      await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
      await create.ExecuteNonQueryAsync();
    }

    var options = new DbContextOptionsBuilder<PermissionsDbContext>().UseNpgsql(target.ConnectionString).Options;
    try
    {
      await using var context = new PermissionsDbContext(options);
      DisposableTestDatabase.ValidateConfiguredTarget(target.ConnectionString);
      var migrator = context.Database.GetService<IMigrator>();
      await migrator.MigrateAsync("20260413092908_PureZanzibar");
      var id = Guid.NewGuid();
      var expiry = DateTime.UtcNow.AddDays(1);
      expiry = expiry.AddTicks(-(expiry.Ticks % 10));
      await context.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO relation_tuples (id, object_type, object_id, relation, subject_type,
            subject_id, subject_relation, created_at, expires_at)
          VALUES ({id}, 'document', 'old', 'viewer', 'group', 'finance', NULL,
            {DateTime.UtcNow}, {expiry})
          """);

      DisposableTestDatabase.ValidateConfiguredTarget(target.ConnectionString);
      await migrator.MigrateAsync();
      var preserved = Assert.Single(await context.RelationTuples.AsNoTracking().ToListAsync());
      Assert.Equal(id, preserved.Id);
      Assert.Equal(expiry, preserved.ExpiresAt);
      await using (var connection = new NpgsqlConnection(target.ConnectionString))
      {
        await connection.OpenAsync();
        await using var indexes = new NpgsqlCommand("""
            SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'relation_tuples'
              AND indexname IN ('ix_relation_tuples_lookup', 'ix_relation_tuples_identity')
            """, connection);
        await using var reader = await indexes.ExecuteReaderAsync();
        var indexesFound = new List<(string Name, string Definition)>();
        while (await reader.ReadAsync()) indexesFound.Add((reader.GetString(0), reader.GetString(1)));
        var newIndex = Assert.Single(indexesFound);
        Assert.Equal("ix_relation_tuples_identity", newIndex.Name);
        Assert.Contains("NULLS NOT DISTINCT", newIndex.Definition);
      }

      await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO relation_tuples (id, object_type, object_id, relation, subject_type,
            subject_id, subject_relation, created_at, expires_at)
          VALUES ({Guid.NewGuid()}, 'document', 'old', 'viewer', 'group', 'finance', NULL,
            {DateTime.UtcNow}, NULL)
          """));

      await context.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO relation_tuples (id, object_type, object_id, relation, subject_type,
            subject_id, subject_relation, created_at, expires_at)
          VALUES ({Guid.NewGuid()}, 'document', 'old', 'viewer', 'group', 'finance', 'member',
            {DateTime.UtcNow}, NULL)
          """);
      DisposableTestDatabase.ValidateConfiguredTarget(target.ConnectionString);
      await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync("20260413092908_PureZanzibar"));
      Assert.Equal(2, await context.RelationTuples.CountAsync());
      Assert.Equal(expiry, (await context.RelationTuples.AsNoTracking()
          .SingleAsync(t => t.Id == id)).ExpiresAt);
      await using (var connection = new NpgsqlConnection(target.ConnectionString))
      {
        await connection.OpenAsync();
        await using var index = new NpgsqlCommand("""
            SELECT count(*) FROM pg_indexes WHERE tablename = 'relation_tuples'
              AND indexname = 'ix_relation_tuples_identity'
            """, connection);
        Assert.Equal(1L, await index.ExecuteScalarAsync());
      }
    }
    finally
    {
      DisposableTestDatabase.ValidateConfiguredTarget(target.ConnectionString);
      await using var context = new PermissionsDbContext(options);
      await context.Database.EnsureDeletedAsync();
    }
  }
}
