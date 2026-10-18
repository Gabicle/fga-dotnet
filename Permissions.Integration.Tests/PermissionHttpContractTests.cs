using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Permissions.Application.DTOs;

namespace Permissions.Integration.Tests;

public sealed partial class PermissionIntegrationTests
{
  [Fact]
  public async Task HttpChecksUseRealPostgresAndEmitOneSafeDecisionPerControllerAction()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "alice", "viewer", "document", "one"));
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "alice", "viewer", "report", "one"));

    using var factory = new PostgresApiFactory(
        DisposableTestDatabase.ValidateConfiguredTarget(DbContext.Database.GetConnectionString()).ConnectionString);
    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);

    var allowed = await client.PostAsJsonAsync("/api/permissions/check", DocumentCheck("alice"));
    var denied = await client.PostAsJsonAsync("/api/permissions/check", DocumentCheck("bob"));
    var abacAllowed = await client.PostAsJsonAsync("/api/permissions/check",
        ReportHttpCheck("private-region-canary"));
    var abacDenied = await client.PostAsJsonAsync("/api/permissions/check", ReportHttpCheck("other-region"));
    var invalid = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation = "viewer",
      objectType = "report", objectId = "one"
    });
    var invalidIdentity = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation = "viewer",
      objectType = "report", objectId = "one",
      subjectAttributes = new { subjectType = "user", subjectId = "other-user",
        region = "private-region-canary", clearanceLevel = 3 },
      resourceAttributes = new { resourceType = "report", resourceId = "one",
        region = "private-region-canary", sensitivityLevel = 2 }
    });

    await AssertDecisionAsync(allowed, HttpStatusCode.OK, true, AuthorizationReasonCodes.Allowed);
    await AssertDecisionAsync(denied, HttpStatusCode.OK, false, AuthorizationReasonCodes.RelationshipDenied);
    await AssertDecisionAsync(abacAllowed, HttpStatusCode.OK, true, AuthorizationReasonCodes.Allowed);
    await AssertDecisionAsync(abacDenied, HttpStatusCode.OK, false, AuthorizationReasonCodes.RequiredAbacDenied);
    await AssertDecisionAsync(invalid, HttpStatusCode.BadRequest, null, AuthorizationReasonCodes.InvalidContext);
    await AssertDecisionAsync(invalidIdentity, HttpStatusCode.BadRequest, null,
        AuthorizationReasonCodes.InvalidContext);

    var events = factory.Decisions.Events;
    Assert.Equal(6, events.Count);
    foreach (var (response, expectedOutcome) in new[] {
      (allowed, "allow"), (denied, "deny"), (abacAllowed, "allow"), (abacDenied, "deny"),
      (invalid, "error"), (invalidIdentity, "error")
    })
    {
      var id = Assert.Single(response.Headers.GetValues("X-Authorization-Decision-Id"));
      var entry = Assert.Single(events, e => e.Fields["DecisionId"]?.ToString() == id);
      Assert.Equal("check-client", entry.Fields["ServiceIdentity"]);
      Assert.Equal(expectedOutcome, entry.Fields["Outcome"]);
      Assert.Equal((int)response.StatusCode, entry.Fields["HttpStatus"]);
      Assert.True(Convert.ToDouble(entry.Fields["DurationMs"]) >= 0);
      Assert.Equal("user", entry.Fields["SubjectType"]);
      Assert.Equal("viewer", entry.Fields["Relation"]);
    }
    Assert.Equal("alice", events[0].Fields["SubjectId"]);
    Assert.Equal("document", events[0].Fields["ObjectType"]);
    Assert.Equal("one", events[0].Fields["ObjectId"]);
    Assert.DoesNotContain(factory.CheckKey, string.Join(' ', events.Select(e => e.Message)));
    Assert.DoesNotContain("private-region-canary", string.Join(' ', events.Select(e => e.Message)));
  }

  [Fact]
  public async Task RequestsRejectedBeforeActionHaveNoDecisionEvent()
  {
    using var factory = new PostgresApiFactory(
        DisposableTestDatabase.ValidateConfiguredTarget(DbContext.Database.GetConnectionString()).ConnectionString);
    using var client = factory.CreateClient();
    var unauthenticated = await client.PostAsJsonAsync("/api/permissions/check", DocumentCheck("alice"));
    Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    Assert.False(unauthenticated.Headers.Contains("X-Authorization-Decision-Id"));

    Authenticate(client, factory.CheckKey);
    var invalidJson = await client.PostAsync("/api/permissions/check",
        new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
    Assert.Equal(HttpStatusCode.BadRequest, invalidJson.StatusCode);
    Assert.False(invalidJson.Headers.Contains("X-Authorization-Decision-Id"));
    Assert.Empty(factory.Decisions.Events);
  }

  [Fact]
  public async Task UnavailableDatabaseReturnsErrorAndNeverAllows()
  {
    var configured = DisposableTestDatabase.ValidateConfiguredTarget(DbContext.Database.GetConnectionString());
    configured.Port = 1;
    configured.Timeout = 2;
    using var factory = new PostgresApiFactory(configured.ConnectionString);
    using var client = factory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(15);
    Authenticate(client, factory.CheckKey);

    var response = await client.PostAsJsonAsync("/api/permissions/check", DocumentCheck("alice"));
    await AssertDecisionAsync(response, HttpStatusCode.InternalServerError, null,
        AuthorizationReasonCodes.EvaluationError);
    var entry = Assert.Single(factory.Decisions.Events);
    Assert.Equal("error", entry.Fields["Outcome"]);
    Assert.Equal(500, entry.Fields["HttpStatus"]);
  }

  [Fact]
  public async Task ExpirationQueryUsesPostgresTimeAndRejectsBoundaryExpiredTuple()
  {
    var generatedSql = DbContext.RelationTuples.Where(tuple =>
        tuple.ObjectType == "document" &&
        (tuple.ExpiresAt == null || tuple.ExpiresAt > DateTime.UtcNow)).ToQueryString();
    Assert.Contains("now()", generatedSql, StringComparison.OrdinalIgnoreCase);

    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "alice", "viewer", "document", "one"));
    await DbContext.Database.ExecuteSqlRawAsync(
        "UPDATE relation_tuples SET expires_at = clock_timestamp() + interval '30 seconds'");
    Assert.True((await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "alice", "viewer", "document", "one"))).Allowed);

    await DbContext.Database.ExecuteSqlRawAsync(
        "UPDATE relation_tuples SET expires_at = clock_timestamp()");
    Assert.False((await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "alice", "viewer", "document", "one"))).Allowed);
  }

  private static object DocumentCheck(string subjectId) => new {
    subjectType = "user", subjectId, relation = "viewer", objectType = "document", objectId = "one"
  };

  private static object ReportHttpCheck(string subjectRegion) => new {
    subjectType = "user", subjectId = "alice", relation = "viewer", objectType = "report", objectId = "one",
    subjectAttributes = new { subjectType = "user", subjectId = "alice", region = subjectRegion,
      clearanceLevel = 3 },
    resourceAttributes = new { resourceType = "report", resourceId = "one", region = "private-region-canary",
      sensitivityLevel = 2 }
  };

  private static void Authenticate(HttpClient client, string key) =>
      client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", key);

  private static async Task AssertDecisionAsync(HttpResponseMessage response, HttpStatusCode status,
      bool? allowed, string reasonCode)
  {
    Assert.Equal(status, response.StatusCode);
    Assert.Single(response.Headers.GetValues("X-Authorization-Decision-Id"));
    using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal(reasonCode, body.RootElement.GetProperty("reasonCode").GetString());
    if (allowed.HasValue)
      Assert.Equal(allowed.Value, body.RootElement.GetProperty("allowed").GetBoolean());
    else
      Assert.False(body.RootElement.TryGetProperty("allowed", out _));
  }

  private sealed class PostgresApiFactory(string connectionString) : WebApplicationFactory<Program>
  {
    public string CheckKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private string AdminKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public DecisionLogProvider Decisions { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
      builder.UseEnvironment("Development");
      builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
          new Dictionary<string, string?> {
            ["Permissions:ApiKeys:Check"] = CheckKey,
            ["Permissions:ApiKeys:Admin"] = AdminKey,
            ["ConnectionStrings:DefaultConnection"] = connectionString
          }));
      builder.ConfigureLogging(logging => logging.AddProvider(Decisions));
    }
  }

  private sealed class DecisionLogProvider : ILoggerProvider
  {
    private readonly ConcurrentQueue<DecisionEntry> _entries = new();
    public IReadOnlyList<DecisionEntry> Events => _entries.ToArray();
    public ILogger CreateLogger(string categoryName) => new DecisionLogger(_entries);
    public void Dispose() { }

    private sealed class DecisionLogger(ConcurrentQueue<DecisionEntry> entries) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
          Exception? exception, Func<TState, Exception?, string> formatter)
      {
        if (eventId.Id != 1001) return;
        var fields = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(
            pair => pair.Key, pair => pair.Value);
        entries.Enqueue(new DecisionEntry(fields, formatter(state, exception)));
      }
    }

    private sealed class NullScope : IDisposable
    {
      public static NullScope Instance { get; } = new();
      public void Dispose() { }
    }
  }

  private sealed record DecisionEntry(IReadOnlyDictionary<string, object?> Fields, string Message);
}
