using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Permissions.Application.DTOs;
using Permissions.Application.Services;
using Permissions.Domain.Attributes;
using Permissions.Domain.Repositories;
using Permissions.Infrastructure.Repositories;

namespace Permissions.Integration.Tests;

public sealed class PermissionIntegrationTests : IntegrationTestBase
{
  private readonly IServiceProvider _services;

  public PermissionIntegrationTests(DatabaseFixture fixture) : base(fixture)
  {
    _services = fixture.Services;
  }

  private static CheckPermissionRequest ReportCheck(string subjectId) => new(
      "user", subjectId, "viewer", "report", "42",
      SubjectAttributes: new SubjectAttributes {
        SubjectType = "user", SubjectId = subjectId, Region = "eu-west", ClearanceLevel = 3
      },
      ResourceAttributes: new ResourceAttributes {
        ResourceType = "report", ResourceId = "42", Region = "eu-west",
        SensitivityLevel = 2, Status = "draft"
      });

  [Fact]
  public async Task Grant_ThenCheck_DirectTuple_ReturnsAllowed()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    var result = await PermissionService.CheckAsync(ReportCheck("7"));

    Assert.True(result.Allowed);
    Assert.Equal("Allowed via tuple expansion", result.Reason);
  }

  [Fact]
  public async Task Grant_ThenRevoke_ThenCheck_ReturnsDenied()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    await PermissionService.RevokeAsync(
        new Domain.ValueObjects.TupleKey("report", "42", "viewer", "user", "7"));

    var result = await PermissionService.CheckAsync(ReportCheck("7"));

    Assert.False(result.Allowed);
  }

  [Fact]
  public async Task Grant_DuplicateTuple_DoesNotThrow()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    var result = await PermissionService.CheckAsync(ReportCheck("7"));

    Assert.True(result.Allowed);
  }

  [Fact]
  public async Task Check_NoTuple_ReturnsDenied()
  {
    var result = await PermissionService.CheckAsync(ReportCheck("7"));

    Assert.False(result.Allowed);
  }

  [Fact]
  public async Task ReportRequiresAttributesWhileUnlistedDocumentRemainsRebacOnly()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42"));
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "document", "42"));

    await Assert.ThrowsAsync<ArgumentException>(() => PermissionService.CheckAsync(
        new CheckPermissionRequest("user", "7", "viewer", "report", "42")));
    Assert.True((await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "document", "42"))).Allowed);
    Assert.True((await PermissionService.CheckAsync(ReportCheck("7"))).Allowed);
  }

  [Fact]
  public async Task ReportViewerRejectsSpoofedNestedResourceType()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42"));
    var valid = ReportCheck("7");
    var spoofed = valid with {
      ResourceAttributes = new ResourceAttributes {
        ResourceType = "document", ResourceId = "42", Region = "eu-west",
        SensitivityLevel = 2
      }
    };

    await Assert.ThrowsAsync<ArgumentException>(() => PermissionService.CheckAsync(spoofed));
    Assert.True((await PermissionService.CheckAsync(valid)).Allowed);
  }

  [Theory]
  [InlineData("locked", false)]
  [InlineData("archived", false)]
  [InlineData("draft", true)]
  public async Task ReportEditorEnforcesPersistedGrantAndStatus(string status, bool expectedAllowed)
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "editor", "report", "42"));
    var viewer = ReportCheck("7");
    var editor = viewer with {
      Relation = "editor",
      ResourceAttributes = new ResourceAttributes {
        ResourceType = "report", ResourceId = "42", Region = "eu-west",
        SensitivityLevel = 2, Status = status
      }
    };

    Assert.Equal(expectedAllowed, (await PermissionService.CheckAsync(editor)).Allowed);
  }

  [Fact]
  public async Task Grant_RoleMembership_ThenCheck_ReturnsAllowed()
  {
    // user:7 is a member of role:editor
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "member", "role", "editor"));

    // role:editor members can view report:42
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "role", "editor", "viewer", "report", "42",
        SubjectRelation: "member"));

    var result = await PermissionService.CheckAsync(ReportCheck("7"));

    Assert.True(result.Allowed);
  }

  [Fact]
  public async Task Grant_RoleMembership_OtherUser_ReturnsDenied()
  {
    // user:7 is a member of role:editor
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "member", "role", "editor"));

    // role:editor members can view report:42
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "role", "editor", "viewer", "report", "42",
        SubjectRelation: "member"));

    // user:99 is NOT a member of role:editor
    var result = await PermissionService.CheckAsync(ReportCheck("99"));

    Assert.False(result.Allowed);
  }

  [Fact]
  public async Task Check_WithMatchingRegion_ReturnsAllowed()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    var result = await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42",
        SubjectAttributes: new SubjectAttributes
        {
          SubjectType = "user",
          SubjectId = "7",
          Region = "eu-west",
          ClearanceLevel = 3
        },
        ResourceAttributes: new ResourceAttributes
        {
          ResourceType = "report",
          ResourceId = "42",
          Region = "eu-west",
          SensitivityLevel = 2
        }));

    Assert.True(result.Allowed);
  }

  [Fact]
  public async Task Check_WithMismatchedRegion_ReturnsDenied()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    var result = await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42",
        SubjectAttributes: new SubjectAttributes
        {
          SubjectType = "user",
          SubjectId = "7",
          Region = "us-east",
          ClearanceLevel = 3
        },
        ResourceAttributes: new ResourceAttributes
        {
          ResourceType = "report",
          ResourceId = "42",
          Region = "eu-west",
          SensitivityLevel = 2
        }));

    Assert.False(result.Allowed);
    Assert.Equal("Denied by ABAC policy: RegionMatchPolicy", result.Reason);
  }

  [Fact]
  public async Task Check_WithLockedResource_ReturnsDenied()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "member", "role", "editor"));

    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "role", "editor", "editor", "report", "42",
        SubjectRelation: "member"));

    var result = await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "7", "editor", "report", "42",
        SubjectAttributes: new SubjectAttributes
        {
          SubjectType = "user",
          SubjectId = "7",
          Region = "eu-west",
          ClearanceLevel = 3
        },
        ResourceAttributes: new ResourceAttributes
        {
          ResourceType = "report",
          ResourceId = "42",
          Region = "eu-west",
          Status = "locked",
          SensitivityLevel = 2
        }));

    Assert.False(result.Allowed);
    Assert.Equal("Denied by ABAC policy: ResourceStatusPolicy", result.Reason);
  }

  [Fact]
  public async Task Check_ExpiredTuple_ReturnsDenied()
  {
    // Grant with expiry 1 millisecond in the future
    var tuple = Domain.Entities.RelationTuple.Create(
        "report", "42", "viewer", "user", "7",
        expiresAt: DateTime.UtcNow.AddMilliseconds(1));

    await DbContext.RelationTuples.AddAsync(tuple);
    await DbContext.SaveChangesAsync();

    // Wait for it to expire
    await Task.Delay(10);

    var result = await PermissionService.CheckAsync(ReportCheck("7"));

    Assert.False(result.Allowed);
  }

  [Fact]
  public async Task Grant_WithExpiry_ThenCheck_AfterExpiry_ReturnsDenied()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42",
        ExpiresAt: DateTime.UtcNow.AddMilliseconds(50)));

    await Task.Delay(100);

    var result = await PermissionService.CheckAsync(ReportCheck("7"));

    Assert.False(result.Allowed);
  }

  [Fact]
  public async Task Grant_WithFutureExpiry_ThenCheck_ReturnsAllowed()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42",
        ExpiresAt: DateTime.UtcNow.AddDays(1)));

    var result = await PermissionService.CheckAsync(ReportCheck("7"));

    Assert.True(result.Allowed);
  }

  [Fact]
  public async Task RoleHierarchy_PersistsAndInheritsOnlyTowardParent()
  {
    var viewer = await RoleService.CreateAsync(new CreateRoleRequest("viewer"));
    var editor = await RoleService.CreateAsync(new CreateRoleRequest("editor", ParentRoleId: viewer.Id));
    var admin = await RoleService.CreateAsync(new CreateRoleRequest("admin", ParentRoleId: editor.Id));
    Assert.Equal(viewer.Id, editor.ParentRoleId);
    Assert.Equal(editor.Id, admin.ParentRoleId);

    await PermissionService.GrantAsync(new GrantPermissionRequest("user", "7", "member", "role", "admin"));
    await PermissionService.GrantAsync(new GrantPermissionRequest("user", "8", "member", "role", "viewer"));
    await PermissionService.GrantAsync(new GrantPermissionRequest("role", "viewer", "viewer", "report", "42", "member"));
    await PermissionService.GrantAsync(new GrantPermissionRequest("role", "admin", "admin", "report", "42", "member"));

    Assert.True((await PermissionService.CheckAsync(ReportCheck("7"))).Allowed);
    Assert.False((await PermissionService.CheckAsync(new CheckPermissionRequest("user", "8", "admin", "report", "42"))).Allowed);
  }

  [Fact]
  public async Task RoleHierarchy_CycleAssignmentIsRejectedAndExistingStatePersists()
  {
    var viewer = await RoleService.CreateAsync(new CreateRoleRequest("viewer"));
    var editor = await RoleService.CreateAsync(new CreateRoleRequest("editor", ParentRoleId: viewer.Id));
    var admin = await RoleService.CreateAsync(new CreateRoleRequest("admin", ParentRoleId: editor.Id));

    await Assert.ThrowsAsync<InvalidOperationException>(() => RoleService.AssignParentAsync(viewer.Id, viewer.Id));
    await Assert.ThrowsAsync<InvalidOperationException>(() => RoleService.AssignParentAsync(viewer.Id, admin.Id));

    Assert.Null((await RoleService.GetByIdAsync(viewer.Id))!.ParentRoleId);
    Assert.Equal(viewer.Id, (await RoleService.GetByIdAsync(editor.Id))!.ParentRoleId);
    Assert.Equal(editor.Id, (await RoleService.GetByIdAsync(admin.Id))!.ParentRoleId);
  }

  [Fact]
  public async Task RoleHierarchy_ReparentingChangesEffectivePermissions()
  {
    var viewer = await RoleService.CreateAsync(new CreateRoleRequest("viewer"));
    var editor = await RoleService.CreateAsync(new CreateRoleRequest("editor"));
    var auditor = await RoleService.CreateAsync(new CreateRoleRequest("auditor", ParentRoleId: editor.Id));
    await PermissionService.GrantAsync(new GrantPermissionRequest("user", "7", "member", "role", "auditor"));
    await PermissionService.GrantAsync(new GrantPermissionRequest("role", "viewer", "viewer", "report", "42", "member"));

    var request = ReportCheck("7");
    Assert.False((await PermissionService.CheckAsync(request)).Allowed);
    await RoleService.AssignParentAsync(auditor.Id, viewer.Id);
    Assert.True((await PermissionService.CheckAsync(request)).Allowed);
    await RoleService.AssignParentAsync(auditor.Id, editor.Id);
    Assert.False((await PermissionService.CheckAsync(request)).Allowed);
  }

  [Fact]
  public async Task RoleHierarchy_MalformedPersistedCycleDeniesInheritanceButPreservesDirectGrant()
  {
    var viewer = await RoleService.CreateAsync(new CreateRoleRequest("viewer"));
    var editor = await RoleService.CreateAsync(new CreateRoleRequest("editor", ParentRoleId: viewer.Id));
    await DbContext.Roles.Where(r => r.Id == viewer.Id)
        .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.ParentRoleId, editor.Id));
    try
    {
      var repository = Scope.ServiceProvider.GetRequiredService<IRoleRepository>();
      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          repository.GetInheritanceChainAsync(viewer.Id));

      await PermissionService.GrantAsync(new GrantPermissionRequest("role", "viewer", "viewer", "report", "42", "member"));
      await PermissionService.GrantAsync(new GrantPermissionRequest("user", "7", "member", "role", "editor"));
      await PermissionService.GrantAsync(new GrantPermissionRequest("user", "8", "viewer", "report", "42"));

      Assert.False((await PermissionService.CheckAsync(ReportCheck("7"))).Allowed);
      Assert.True((await PermissionService.CheckAsync(ReportCheck("8"))).Allowed);
    }
    finally
    {
      await DbContext.Roles.Where(r => r.Id == viewer.Id)
          .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.ParentRoleId, (Guid?)null));
    }
  }

  [Fact]
  public async Task ConcurrentOpposingParentAssignmentsCannotCommitACycle()
  {
    var a = await RoleService.CreateAsync(new CreateRoleRequest("A"));
    var b = await RoleService.CreateAsync(new CreateRoleRequest("B"));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await using var holder = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await holder.OpenAsync(timeout.Token);
    await using var heldTransaction = await holder.BeginTransactionAsync(timeout.Token);
    await AcquireHierarchyLockAsync(holder, heldTransaction, timeout.Token);

    var first = TryAssignInNewScopeAsync(a.Id, b.Id, timeout.Token);
    var second = TryAssignInNewScopeAsync(b.Id, a.Id, timeout.Token);
    try
    {
      await WaitForAdvisoryWaitersAsync(2, timeout.Token);
    }
    finally
    {
      await heldTransaction.CommitAsync(CancellationToken.None);
    }

    var outcomes = await Task.WhenAll(first, second).WaitAsync(timeout.Token);
    Assert.Single(outcomes, error => error is null);
    Assert.Single(outcomes, error => error is InvalidOperationException);

    var persistedA = (await RoleService.GetByIdAsync(a.Id))!;
    var persistedB = (await RoleService.GetByIdAsync(b.Id))!;
    Assert.True((persistedA.ParentRoleId == b.Id && persistedB.ParentRoleId is null) ||
        (persistedB.ParentRoleId == a.Id && persistedA.ParentRoleId is null));

    var repository = Scope.ServiceProvider.GetRequiredService<IRoleRepository>();
    Assert.InRange((await repository.GetInheritanceChainAsync(a.Id)).Count, 1, 2);
    Assert.InRange((await repository.GetInheritanceChainAsync(b.Id)).Count, 1, 2);

    // A later independent assignment that does not form a cycle still succeeds.
    var c = await RoleService.CreateAsync(new CreateRoleRequest("C"));
    await AssignInNewScopeAsync(c.Id, a.Id, timeout.Token);
    Assert.Equal(a.Id, (await RoleService.GetByIdAsync(c.Id))!.ParentRoleId);
  }

  [Fact]
  public async Task ParentedRoleCreationWaitsForHierarchyLock()
  {
    var parent = await RoleService.CreateAsync(new CreateRoleRequest("parent"));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await using var holder = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await holder.OpenAsync(timeout.Token);
    await using var heldTransaction = await holder.BeginTransactionAsync(timeout.Token);
    await AcquireHierarchyLockAsync(holder, heldTransaction, timeout.Token);

    var creation = CreateInNewScopeAsync(new CreateRoleRequest("child", ParentRoleId: parent.Id), timeout.Token);
    try
    {
      await WaitForAdvisoryWaitersAsync(1, timeout.Token);
      Assert.DoesNotContain(await RoleService.GetAllAsync(), role => role.Name == "child");
    }
    finally
    {
      await heldTransaction.CommitAsync(CancellationToken.None);
    }

    var child = await creation.WaitAsync(timeout.Token);
    Assert.Equal(parent.Id, child.ParentRoleId);
  }

  [Fact]
  public async Task FailedValidationRollsBackAndReleasesHierarchyLock()
  {
    var a = await RoleService.CreateAsync(new CreateRoleRequest("A"));
    var b = await RoleService.CreateAsync(new CreateRoleRequest("B"));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        AssignInNewScopeAsync(a.Id, a.Id, timeout.Token));
    Assert.Null((await RoleService.GetByIdAsync(a.Id))!.ParentRoleId);

    await AssignInNewScopeAsync(a.Id, b.Id, timeout.Token).WaitAsync(timeout.Token);
    Assert.Equal(b.Id, (await RoleService.GetByIdAsync(a.Id))!.ParentRoleId);
  }

  [Fact]
  public async Task FailureAfterPersistenceRollsBackParentChangeAndReleasesLock()
  {
    var a = await RoleService.CreateAsync(new CreateRoleRequest("A"));
    var b = await RoleService.CreateAsync(new CreateRoleRequest("B"));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

    using (var scope = _services.CreateScope())
    {
      var repository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          repository.ExecuteHierarchyMutationAsync<bool>(async token =>
          {
            var role = (await repository.GetByIdAsync(a.Id, token))!;
            var parent = (await repository.GetByIdAsync(b.Id, token))!;
            role.SetParent(parent);
            await repository.UpdateAsync(role, token);
            throw new InvalidOperationException("forced transaction failure");
          }, timeout.Token));
    }

    Assert.Null((await RoleService.GetByIdAsync(a.Id))!.ParentRoleId);
    await AssignInNewScopeAsync(a.Id, b.Id, timeout.Token).WaitAsync(timeout.Token);
    Assert.Equal(b.Id, (await RoleService.GetByIdAsync(a.Id))!.ParentRoleId);
  }

  [Fact]
  public async Task CanceledLockWaitDoesNotLeaveAMutationOrHeldLock()
  {
    var a = await RoleService.CreateAsync(new CreateRoleRequest("A"));
    var b = await RoleService.CreateAsync(new CreateRoleRequest("B"));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    using var canceledRequest = new CancellationTokenSource();
    await using var holder = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await holder.OpenAsync(timeout.Token);
    await using var heldTransaction = await holder.BeginTransactionAsync(timeout.Token);
    await AcquireHierarchyLockAsync(holder, heldTransaction, timeout.Token);

    var assignment = AssignInNewScopeAsync(a.Id, b.Id, canceledRequest.Token);
    try
    {
      await WaitForAdvisoryWaitersAsync(1, timeout.Token);
      canceledRequest.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => assignment.WaitAsync(timeout.Token));
    }
    finally
    {
      await heldTransaction.CommitAsync(CancellationToken.None);
    }

    Assert.Null((await RoleService.GetByIdAsync(a.Id))!.ParentRoleId);
    await AssignInNewScopeAsync(a.Id, b.Id, timeout.Token).WaitAsync(timeout.Token);
    Assert.Equal(b.Id, (await RoleService.GetByIdAsync(a.Id))!.ParentRoleId);
  }

  private async Task AssignInNewScopeAsync(Guid roleId, Guid parentId, CancellationToken token)
  {
    using var scope = _services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<RoleService>()
        .AssignParentAsync(roleId, parentId, token);
  }

  private async Task<Exception?> TryAssignInNewScopeAsync(Guid roleId, Guid parentId, CancellationToken token)
  {
    try
    {
      await AssignInNewScopeAsync(roleId, parentId, token);
      return null;
    }
    catch (Exception error)
    {
      return error;
    }
  }

  private async Task<RoleResponse> CreateInNewScopeAsync(CreateRoleRequest request, CancellationToken token)
  {
    using var scope = _services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<RoleService>().CreateAsync(request, token);
  }

  private static async Task AcquireHierarchyLockAsync(
      NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
  {
    await using var command = new NpgsqlCommand(
        "SELECT pg_advisory_xact_lock(@key)", connection, transaction);
    command.Parameters.AddWithValue("key", RoleRepository.HierarchyMutationLockKey);
    await command.ExecuteNonQueryAsync(token);
  }

  private async Task WaitForAdvisoryWaitersAsync(int expected, CancellationToken token)
  {
    await using var monitor = new NpgsqlConnection(DbContext.Database.GetConnectionString());
    await monitor.OpenAsync(token);
    while (true)
    {
      await using var command = new NpgsqlCommand(
          "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' " +
          "AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())",
          monitor);
      var count = (long)(await command.ExecuteScalarAsync(token))!;
      if (count >= expected) return;
      await Task.Delay(25, token);
    }
  }
}

