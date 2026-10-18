using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Permissions.Application.DTOs;
using Permissions.Application.Policies;
using Permissions.Application.Policies.BuiltIn;
using Permissions.Domain.Attributes;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Api.Tests;

public sealed class AuthenticationHttpTests
{
  [Fact]
  public async Task AdminGrantClassifiesIdempotenceConflictAndInvalidExpiration()
  {
    using var factory = new AuthApiFactory();
    using var client = factory.CreateClient();
    Authenticate(client, factory.AdminKey);
    var expiry = DateTime.UtcNow.AddDays(1);
    var body = new GrantPermissionRequest("user", "alice", "viewer", "document", "one", ExpiresAt: expiry);

    Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/permissions/grant", body)).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/permissions/grant", body)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/permissions/grant",
        body with { ExpiresAt = expiry.AddHours(1) })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/permissions/grant",
        body with { ExpiresAt = null })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/permissions/grant",
        body with { ExpiresAt = DateTime.UtcNow.AddDays(-1) })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/permissions/grant",
        new { subjectType = "user", subjectId = "alice", relation = "viewer",
          objectType = "document", objectId = "one", expiresAt = "2030-01-01T00:00:00" })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/permissions/grant",
        body with { SubjectRelation = " " })).StatusCode);
    Assert.Equal(expiry.AddTicks(-(expiry.Ticks % 10)), Assert.Single(factory.Tuples).ExpiresAt);
  }

  [Fact]
  public async Task AdminRevokeUsesSubjectRelationAndRejectsBlankRelation()
  {
    using var factory = new AuthApiFactory();
    factory.Tuples.Add(RelationTuple.Create("document", "one", "viewer", "group", "finance"));
    factory.Tuples.Add(RelationTuple.Create("document", "one", "viewer", "group", "finance", "member"));
    using var client = factory.CreateClient();
    Authenticate(client, factory.AdminKey);
    var direct = new RevokePermissionRequest("group", "finance", "viewer", "document", "one");

    Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/permissions/revoke", direct)).StatusCode);
    Assert.Equal("member", Assert.Single(factory.Tuples).SubjectRelation);
    Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/permissions/revoke", direct)).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/permissions/revoke",
        direct with { SubjectRelation = " " })).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/permissions/revoke",
        direct with { SubjectRelation = "member" })).StatusCode);
    Assert.Empty(factory.Tuples);
  }

  private static readonly object CheckBody = new {
    subjectType = "user", subjectId = "alice", relation = "viewer",
    objectType = "report", objectId = "one",
    subjectAttributes = new { subjectType = "user", subjectId = "alice",
      region = "eu-west", clearanceLevel = 3 },
    resourceAttributes = new { resourceType = "report", resourceId = "one",
      region = "eu-west", sensitivityLevel = 2 }
  };

  [Fact]
  public async Task MissingCredentialsAreRejectedOnAllProtectedRoutes()
  {
    using var factory = new AuthApiFactory();
    using var client = factory.CreateClient();

    Assert.Equal(HttpStatusCode.Unauthorized,
        (await client.PostAsJsonAsync("/api/permissions/check", CheckBody)).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await client.PostAsJsonAsync("/api/permissions/grant", CheckBody)).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await client.GetAsync("/api/roles")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await client.GetAsync("/openapi/v1.json")).StatusCode);
  }

  [Fact]
  public async Task InvalidAndMalformedCredentialsAreRejected()
  {
    using var factory = new AuthApiFactory();
    using var client = factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "ApiKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await client.PostAsJsonAsync("/api/permissions/check", CheckBody)).StatusCode);

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CheckKey);
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await client.GetAsync("/api/roles")).StatusCode);

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", "short");
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await client.GetAsync("/openapi/v1.json")).StatusCode);
  }

  [Fact]
  public async Task CheckClientCannotMutatePermissionsOrRoles()
  {
    using var factory = new AuthApiFactory();
    var existingGrant = RelationTuple.Create("report", "one", "viewer", "user", "alice");
    factory.Tuples.Add(existingGrant);
    var parent = Role.Create("parent");
    var alternativeParent = Role.Create("alternative-parent");
    var child = Role.Create("child");
    child.SetParent(parent);
    factory.Roles.AddRange([parent, alternativeParent, child]);

    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);
    client.DefaultRequestHeaders.Add("X-Capability", "permissions.admin");
    client.DefaultRequestHeaders.Add("X-Role", "admin");

    var grant = new {
      subjectType = "user", subjectId = "alice", relation = "viewer",
      objectType = "report", objectId = "new", capability = "permissions.admin"
    };
    var revoke = new {
      subjectType = "user", subjectId = "alice", relation = "viewer",
      objectType = "report", objectId = "one", capability = "permissions.admin"
    };
    Assert.Equal(HttpStatusCode.Forbidden,
        (await client.PostAsJsonAsync("/api/permissions/grant", grant)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,
        (await client.PostAsJsonAsync("/api/permissions/revoke", revoke)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,
        (await client.PostAsJsonAsync("/api/roles", new { name = "forged", capability = "permissions.admin" })).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,
        (await client.GetAsync("/api/roles")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,
        (await client.GetAsync($"/api/roles/{child.Id}")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,
        (await client.PostAsJsonAsync($"/api/roles/{child.Id}/parent", new {
          parentRoleId = alternativeParent.Id, capability = "permissions.admin"
        })).StatusCode);

    Assert.Same(existingGrant, Assert.Single(factory.Tuples));
    Assert.Equal(parent.Id, child.ParentRoleId);
    Assert.Equal(parent.Id, child.ParentRole?.Id);
    Assert.Equal(3, factory.Roles.Count);
    var check = await client.PostAsJsonAsync("/api/permissions/check", CheckBody);
    Assert.True((await check.Content.ReadFromJsonAsync<CheckPermissionResponse>())?.Allowed);
  }

  [Fact]
  public async Task CheckClientReceivesAllowAndDenyDecisions()
  {
    using var factory = new AuthApiFactory();
    factory.Tuples.Add(RelationTuple.Create("report", "one", "viewer", "user", "alice"));
    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);

    var allowed = await (await client.PostAsJsonAsync("/api/permissions/check", CheckBody))
        .Content.ReadFromJsonAsync<CheckPermissionResponse>();
    var denied = await (await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "bob", relation = "viewer",
      objectType = "report", objectId = "one",
      subjectAttributes = new { subjectType = "user", subjectId = "bob",
        region = "eu-west", clearanceLevel = 3 },
      resourceAttributes = new { resourceType = "report", resourceId = "one",
        region = "eu-west", sensitivityLevel = 2 }
    })).Content.ReadFromJsonAsync<CheckPermissionResponse>();

    Assert.True(allowed?.Allowed);
    Assert.Equal(AuthorizationReasonCodes.Allowed, allowed?.ReasonCode);
    Assert.False(denied?.Allowed);
    Assert.Equal(AuthorizationReasonCodes.RelationshipDenied, denied?.ReasonCode);
  }

  [Fact]
  public async Task RoleParentErrorsDistinguishMissingRolesFromHierarchyConflicts()
  {
    using var factory = new AuthApiFactory();
    var existing = Role.Create("existing");
    factory.Roles.Add(existing);
    using var client = factory.CreateClient();
    Authenticate(client, factory.AdminKey);

    var missingRole = await client.PostAsJsonAsync(
        $"/api/roles/{Guid.NewGuid()}/parent", new { parentRoleId = existing.Id });
    var missingParent = await client.PostAsJsonAsync(
        $"/api/roles/{existing.Id}/parent", new { parentRoleId = Guid.NewGuid() });
    var cycle = await client.PostAsJsonAsync(
        $"/api/roles/{existing.Id}/parent", new { parentRoleId = existing.Id });
    var createWithMissingParent = await client.PostAsJsonAsync(
        "/api/roles", new { name = "orphan", parentRoleId = Guid.NewGuid() });

    Assert.Equal(HttpStatusCode.NotFound, missingRole.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, missingParent.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, createWithMissingParent.StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, cycle.StatusCode);
    Assert.Null(existing.ParentRoleId);
    Assert.Single(factory.Roles);
  }

  [Fact]
  public async Task RebacOnlyDocumentCheckWorksWithoutAttributes()
  {
    using var factory = new AuthApiFactory();
    factory.Tuples.Add(RelationTuple.Create("document", "one", "viewer", "user", "alice"));
    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);

    var allowed = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation = "viewer",
      objectType = "document", objectId = "one"
    });
    var denied = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "bob", relation = "viewer",
      objectType = "document", objectId = "one"
    });
    Assert.True((await allowed.Content.ReadFromJsonAsync<CheckPermissionResponse>())!.Allowed);
    Assert.False((await denied.Content.ReadFromJsonAsync<CheckPermissionResponse>())!.Allowed);
  }

  [Theory]
  [InlineData("Report", "viewer")]
  [InlineData("report", "Viewer")]
  [InlineData("reports", "viewer")]
  public async Task VariantCannotReuseCanonicalReportGrant(string objectType, string relation)
  {
    using var factory = new AuthApiFactory();
    factory.Tuples.Add(RelationTuple.Create("report", "one", "viewer", "user", "alice"));
    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);

    var response = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation, objectType, objectId = "one"
    });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.False((await response.Content.ReadFromJsonAsync<CheckPermissionResponse>())!.Allowed);
  }

  [Theory]
  [InlineData("Report", "viewer")]
  [InlineData("report", "Viewer")]
  [InlineData("reports", "viewer")]
  public async Task SeparatelyGrantedVariantRemainsRebacOnly(string objectType, string relation)
  {
    using var factory = new AuthApiFactory();
    factory.Tuples.Add(RelationTuple.Create(objectType, "one", relation, "user", "alice"));
    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);

    var response = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation, objectType, objectId = "one"
    });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.True((await response.Content.ReadFromJsonAsync<CheckPermissionResponse>())!.Allowed);
  }

  [Theory]
  [InlineData("viewer")]
  [InlineData("editor")]
  public async Task CanonicalReportOperationsStillRequireAndEnforceAbac(string relation)
  {
    using var factory = new AuthApiFactory();
    factory.Tuples.Add(RelationTuple.Create("report", "one", relation, "user", "alice"));
    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);

    var missing = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation,
      objectType = "report", objectId = "one"
    });
    var denied = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation,
      objectType = "report", objectId = "one",
      subjectAttributes = new { subjectType = "user", subjectId = "alice",
        region = "us-east", clearanceLevel = 3 },
      resourceAttributes = new { resourceType = "report", resourceId = "one",
        region = "eu-west", sensitivityLevel = 2, status = "draft" }
    });
    var allowed = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation,
      objectType = "report", objectId = "one",
      subjectAttributes = new { subjectType = "user", subjectId = "alice",
        region = "eu-west", clearanceLevel = 3 },
      resourceAttributes = new { resourceType = "report", resourceId = "one",
        region = "eu-west", sensitivityLevel = 2, status = "draft" }
    });

    Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
    Assert.False((await denied.Content.ReadFromJsonAsync<CheckPermissionResponse>())!.Allowed);
    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    Assert.True((await allowed.Content.ReadFromJsonAsync<CheckPermissionResponse>())!.Allowed);
  }

  [Fact]
  public async Task RebacOnlyOptionalAttributesDoNotActivatePolicies()
  {
    using var factory = new AuthApiFactory();
    factory.Tuples.Add(RelationTuple.Create("document", "one", "viewer", "user", "alice"));
    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);

    var allowed = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation = "viewer",
      objectType = "document", objectId = "one",
      subjectAttributes = new { subjectType = "user", subjectId = "alice",
        region = "us-east", clearanceLevel = 1 },
      resourceAttributes = new { resourceType = "document", resourceId = "one",
        region = "eu-west", sensitivityLevel = 2, status = "locked" }
    });
    var noGrant = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "bob", relation = "viewer",
      objectType = "document", objectId = "one",
      subjectAttributes = new { subjectType = "user", subjectId = "bob",
        region = "eu-west", clearanceLevel = 3 },
      resourceAttributes = new { resourceType = "document", resourceId = "one",
        region = "eu-west", sensitivityLevel = 2 }
    });

    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    Assert.True((await allowed.Content.ReadFromJsonAsync<CheckPermissionResponse>())!.Allowed);
    Assert.Equal(HttpStatusCode.OK, noGrant.StatusCode);
    Assert.False((await noGrant.Content.ReadFromJsonAsync<CheckPermissionResponse>())!.Allowed);
  }

  [Theory]
  [InlineData("subject")]
  [InlineData("resource")]
  [InlineData("resource-type")]
  public async Task RebacOnlySuppliedIdentityMismatchIsRejected(string mismatch)
  {
    using var factory = new AuthApiFactory();
    factory.Tuples.Add(RelationTuple.Create("document", "one", "viewer", "user", "alice"));
    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);

    var response = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation = "viewer",
      objectType = "document", objectId = "one",
      subjectAttributes = new { subjectType = "user",
        subjectId = mismatch == "subject" ? "bob" : "alice" },
      resourceAttributes = new {
        resourceType = mismatch == "resource-type" ? "report" : "document",
        resourceId = mismatch == "resource" ? "other" : "one"
      }
    });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Theory]
  [InlineData("subject-object")]
  [InlineData("resource-object")]
  [InlineData("subject-region")]
  [InlineData("sensitivity")]
  [InlineData("subject-identity")]
  [InlineData("resource-identity")]
  [InlineData("resource-type")]
  [InlineData("unsupported-status")]
  public async Task RequiredAbacMalformedInputsReturnBadRequest(string scenario)
  {
    using var factory = new AuthApiFactory();
    factory.Tuples.Add(RelationTuple.Create("report", "one", "editor", "user", "alice"));
    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);
    object? subject = scenario == "subject-object" ? null : new {
      subjectType = "user", subjectId = scenario == "subject-identity" ? "bob" : "alice",
      region = scenario == "subject-region" ? null : "eu-west", clearanceLevel = (int?)3
    };
    object? resource = scenario == "resource-object" ? null : new {
      resourceType = scenario == "resource-type" ? "document" : "report",
      resourceId = scenario == "resource-identity" ? "other" : "one",
      region = "eu-west", sensitivityLevel = scenario == "sensitivity" ? null : (int?)2,
      status = scenario == "unsupported-status" ? "unrecognized" : "draft"
    };

    var response = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation = "editor",
      objectType = "report", objectId = "one",
      subjectAttributes = subject, resourceAttributes = resource
    });
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Theory]
  [InlineData("cross-region", "viewer")]
  [InlineData("low-clearance", "viewer")]
  [InlineData("locked", "editor")]
  [InlineData("archived", "editor")]
  public async Task RequiredAbacPolicyDenialsReturnAllowedFalse(string scenario, string relation)
  {
    using var factory = new AuthApiFactory();
    factory.Tuples.Add(RelationTuple.Create("report", "one", relation, "user", "alice"));
    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);
    var response = await client.PostAsJsonAsync("/api/permissions/check", new {
      subjectType = "user", subjectId = "alice", relation,
      objectType = "report", objectId = "one",
      subjectAttributes = new { subjectType = "user", subjectId = "alice",
        region = scenario == "cross-region" ? "us-east" : "eu-west",
        clearanceLevel = scenario == "low-clearance" ? 1 : 3 },
      resourceAttributes = new { resourceType = "report", resourceId = "one",
        region = "eu-west", sensitivityLevel = 2,
        status = scenario is "locked" or "archived" ? scenario : "draft" }
    });
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.False((await response.Content.ReadFromJsonAsync<CheckPermissionResponse>())!.Allowed);
  }

  [Fact]
  public void MissingRequiredPolicyFailsAtApiStartup()
  {
    using var factory = new AuthApiFactory { RemoveViewerSensitivityPolicy = true };
    var error = Assert.Throws<InvalidOperationException>(() => _ = factory.Server);
    Assert.Contains("Required ABAC policy registration", error.Message);
  }

  [Fact]
  public async Task EvaluatorExceptionDoesNotReturnAuthorizationAllow()
  {
    using var factory = new AuthApiFactory { AddThrowingPolicy = true };
    factory.Tuples.Add(RelationTuple.Create("report", "one", "viewer", "user", "alice"));
    using var client = factory.CreateClient();
    Authenticate(client, factory.CheckKey);

    var response = await client.PostAsJsonAsync("/api/permissions/check", CheckBody);
    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
  }

  [Fact]
  public async Task AdminClientCanManagePermissionsAndRolesButCannotCheck()
  {
    using var factory = new AuthApiFactory();
    using var client = factory.CreateClient();
    Authenticate(client, factory.AdminKey);
    var tuple = new { subjectType = "user", subjectId = "alice", relation = "viewer",
      objectType = "report", objectId = "one" };

    Assert.Equal(HttpStatusCode.NoContent,
        (await client.PostAsJsonAsync("/api/permissions/grant", tuple)).StatusCode);
    Assert.Single(factory.Tuples);
    Assert.Equal(HttpStatusCode.NoContent,
        (await client.PostAsJsonAsync("/api/permissions/revoke", tuple)).StatusCode);
    Assert.Empty(factory.Tuples);

    var created = await client.PostAsJsonAsync("/api/roles", new { name = "owner" });
    Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    Assert.Single(factory.Roles);
    var role = factory.Roles.Single();
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/roles")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/roles/{role.Id}")).StatusCode);
    Assert.Equal(HttpStatusCode.Created,
        (await client.PostAsJsonAsync("/api/roles", new { name = "child" })).StatusCode);
    var child = factory.Roles.Single(r => r.Name == "child");
    Assert.Equal(HttpStatusCode.NoContent,
        (await client.PostAsJsonAsync($"/api/roles/{child.Id}/parent", new { parentRoleId = role.Id })).StatusCode);
    Assert.Equal(role.Id, child.ParentRoleId);
    Assert.Equal(HttpStatusCode.Forbidden,
        (await client.PostAsJsonAsync("/api/permissions/check", CheckBody)).StatusCode);
  }

  [Fact]
  public async Task OpenApiRequiresAuthenticationAndAcceptsEitherClient()
  {
    using var factory = new AuthApiFactory();
    using var client = factory.CreateClient();
    foreach (var key in new[] { factory.CheckKey, factory.AdminKey })
    {
      Authenticate(client, key);
      var response = await client.GetAsync("/openapi/v1.json");
      Assert.Equal(HttpStatusCode.OK, response.StatusCode);
      Assert.Contains("\"openapi\"", await response.Content.ReadAsStringAsync());
    }
  }

  [Theory]
  [InlineData("both missing", "Required API-key configuration")]
  [InlineData("check missing", "Permissions:ApiKeys:Check")]
  [InlineData("admin missing", "Permissions:ApiKeys:Admin")]
  [InlineData("invalid Base64", "Base64-encoded 32-byte key")]
  [InlineData("incorrect length", "Base64-encoded 32-byte key")]
  [InlineData("identical", "must differ")]
  [InlineData("all zero", "degenerate key material")]
  [InlineData("all identical nonzero", "degenerate key material")]
  public void InvalidConfiguredKeysFailDuringStartup(string scenario, string expectedMessage)
  {
    using var factory = new AuthApiFactory();
    switch (scenario)
    {
      case "both missing": factory.CheckKey = ""; factory.AdminKey = ""; break;
      case "check missing": factory.CheckKey = ""; break;
      case "admin missing": factory.AdminKey = ""; break;
      case "invalid Base64": factory.CheckKey = "not-base64"; break;
      case "incorrect length": factory.AdminKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(31)); break;
      case "identical": factory.AdminKey = factory.CheckKey; break;
      case "all zero": factory.AdminKey = Convert.ToBase64String(new byte[32]); break;
      case "all identical nonzero": factory.CheckKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x5A, 32).ToArray()); break;
      default: throw new ArgumentOutOfRangeException(nameof(scenario));
    }

    // Accessing Server constructs and starts the host without sending an HTTP request.
    var error = Assert.Throws<InvalidOperationException>(() => _ = factory.Server);
    Assert.Contains(expectedMessage, error.Message);
    if (!string.IsNullOrEmpty(factory.CheckKey))
      Assert.DoesNotContain(factory.CheckKey, error.Message);
    if (!string.IsNullOrEmpty(factory.AdminKey))
      Assert.DoesNotContain(factory.AdminKey, error.Message);
  }

  [Fact]
  public void IndependentlyGeneratedKeysAllowStartup()
  {
    using var factory = new AuthApiFactory();
    Assert.NotEqual(factory.CheckKey, factory.AdminKey);
    Assert.NotNull(factory.Server);
  }

  [Fact]
  public async Task FallbackPolicyProtectsEndpointWithoutAuthorizationMetadata()
  {
    using var factory = new AuthApiFactory();
    using var client = factory.CreateClient();
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await client.GetAsync("/__test/fallback")).StatusCode);

    Authenticate(client, Convert.ToBase64String(new byte[32]));
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await client.GetAsync("/__test/fallback")).StatusCode);

    Authenticate(client, factory.CheckKey);
    Assert.Equal(HttpStatusCode.OK,
        (await client.GetAsync("/__test/fallback")).StatusCode);
    Authenticate(client, factory.AdminKey);
    Assert.Equal(HttpStatusCode.OK,
        (await client.GetAsync("/__test/fallback")).StatusCode);
  }

  private static void Authenticate(HttpClient client, string key) =>
      client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", key);

  private sealed class AuthApiFactory : WebApplicationFactory<Program>
  {
    public string CheckKey { get; set; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string AdminKey { get; set; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public List<RelationTuple> Tuples { get; } = [];
    public List<Role> Roles { get; } = [];
    public bool RemoveViewerSensitivityPolicy { get; set; }
    public bool AddThrowingPolicy { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
      builder.UseEnvironment("Development");
      builder.ConfigureAppConfiguration((_, configuration) =>
          configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Permissions:ApiKeys:Check"] = CheckKey,
            ["Permissions:ApiKeys:Admin"] = AdminKey
          }));
      builder.ConfigureTestServices(services =>
      {
        if (RemoveViewerSensitivityPolicy)
        {
          var registration = services.Single(descriptor =>
              descriptor.ServiceType == typeof(IAbacPolicy) &&
              descriptor.ImplementationInstance is SensitivityLevelPolicy policy &&
              policy.ResourceType == "report" && policy.Relation == "viewer");
          services.Remove(registration);
        }
        if (AddThrowingPolicy)
          services.AddSingleton<IAbacPolicy>(new ThrowingPolicy());
        services.AddControllers().AddApplicationPart(typeof(FallbackProbeController).Assembly);
        services.RemoveAll<IRelationTupleRepository>();
        services.RemoveAll<IRoleRepository>();

        var tuples = new Mock<IRelationTupleRepository>();
        tuples.Setup(x => x.ExistsAsync(It.IsAny<TupleKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TupleKey key, CancellationToken _) => Tuples.Any(t => Matches(t, key) && !t.IsExpired()));
        tuples.Setup(x => x.GetByObjectAndRelationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string type, string id, string relation, CancellationToken _) =>
                (IReadOnlyList<RelationTuple>)Tuples.Where(t => t.ObjectType == type && t.ObjectId == id && t.Relation == relation && !t.IsExpired()).ToList());
        tuples.Setup(x => x.GrantAsync(It.IsAny<RelationTuple>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RelationTuple tuple, CancellationToken _) =>
            {
              var existing = Tuples.FirstOrDefault(t => Matches(t, new TupleKey(
                  tuple.ObjectType, tuple.ObjectId, tuple.Relation, tuple.SubjectType,
                  tuple.SubjectId, tuple.SubjectRelation)));
              if (existing is null || existing.IsExpired())
              {
                if (existing is not null) Tuples.Remove(existing);
                Tuples.Add(tuple);
                return TupleGrantOutcome.Success;
              }
              return existing.ExpiresAt == tuple.ExpiresAt
                  ? TupleGrantOutcome.Success
                  : TupleGrantOutcome.ActiveExpirationConflict;
            });
        tuples.Setup(x => x.DeleteAsync(It.IsAny<TupleKey>(), It.IsAny<CancellationToken>()))
            .Callback((TupleKey key, CancellationToken _) => Tuples.RemoveAll(t => Matches(t, key)))
            .Returns(Task.CompletedTask);

        var roles = new Mock<IRoleRepository>();
        roles.Setup(x => x.ExecuteHierarchyMutationAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken token) => operation(token));
        roles.Setup(x => x.ExecuteHierarchyMutationAsync(
                It.IsAny<Func<CancellationToken, Task<RoleResponse>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<RoleResponse>> operation, CancellationToken token) => operation(token));
        roles.Setup(x => x.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => Roles.FirstOrDefault(r => r.NormalizedName == name));
        roles.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Roles.FirstOrDefault(r => r.Id == id));
        roles.Setup(x => x.GetChildrenByParentIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                (IReadOnlyList<Role>)Roles.Where(r => r.ParentRoleId == id).ToList());
        roles.Setup(x => x.GetInheritanceChainAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
            {
              var chain = new List<Role>();
              var visited = new HashSet<Guid>();
              var current = Roles.FirstOrDefault(r => r.Id == id);
              while (current is not null)
              {
                if (!visited.Add(current.Id))
                  throw new InvalidOperationException("Role hierarchy contains a cycle.");
                chain.Add(current);
                current = current.ParentRoleId.HasValue
                    ? Roles.FirstOrDefault(r => r.Id == current.ParentRoleId.Value)
                    : null;
              }
              return (IReadOnlyList<Role>)chain;
            });
        roles.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (IReadOnlyList<Role>)Roles.ToList());
        roles.Setup(x => x.AddAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()))
            .Callback((Role role, CancellationToken _) => Roles.Add(role))
            .Returns(Task.CompletedTask);
        roles.Setup(x => x.UpdateAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        services.AddSingleton(tuples.Object);
        services.AddSingleton(roles.Object);
      });
    }

    private static bool Matches(RelationTuple tuple, TupleKey key) =>
        tuple.ObjectType == key.ObjectType && tuple.ObjectId == key.ObjectId &&
        tuple.Relation == key.Relation && tuple.SubjectType == key.SubjectType &&
        tuple.SubjectId == key.SubjectId && tuple.SubjectRelation == key.SubjectRelation;
  }

  private sealed class ThrowingPolicy : IAbacPolicy
  {
    public string Name => "ThrowingPolicy";
    public string ResourceType => "report";
    public string Relation => "viewer";
    public bool Evaluate(SubjectAttributes subject, ResourceAttributes resource,
        EnvironmentAttributes environment) => throw new InvalidOperationException("Policy failed");
  }
}

[ApiController]
[Route("__test/fallback")]
public sealed class FallbackProbeController : ControllerBase
{
  [HttpGet]
  public IActionResult Get() => Ok();
}
