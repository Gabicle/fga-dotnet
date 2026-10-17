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
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Api.Tests;

public sealed class AuthenticationHttpTests
{
  private static readonly object CheckBody = new {
    subjectType = "user", subjectId = "alice", relation = "viewer",
    objectType = "report", objectId = "one"
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
      objectType = "report", objectId = "one"
    })).Content.ReadFromJsonAsync<CheckPermissionResponse>();

    Assert.True(allowed?.Allowed);
    Assert.False(denied?.Allowed);
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
        services.AddControllers().AddApplicationPart(typeof(FallbackProbeController).Assembly);
        services.RemoveAll<IRelationTupleRepository>();
        services.RemoveAll<IRoleRepository>();

        var tuples = new Mock<IRelationTupleRepository>();
        tuples.Setup(x => x.ExistsAsync(It.IsAny<TupleKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TupleKey key, CancellationToken _) => Tuples.Any(t => Matches(t, key) && !t.IsExpired()));
        tuples.Setup(x => x.GetByObjectAndRelationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string type, string id, string relation, CancellationToken _) =>
                (IReadOnlyList<RelationTuple>)Tuples.Where(t => t.ObjectType == type && t.ObjectId == id && t.Relation == relation && !t.IsExpired()).ToList());
        tuples.Setup(x => x.AddAsync(It.IsAny<RelationTuple>(), It.IsAny<CancellationToken>()))
            .Callback((RelationTuple tuple, CancellationToken _) => Tuples.Add(tuple))
            .Returns(Task.CompletedTask);
        tuples.Setup(x => x.DeleteAsync(It.IsAny<TupleKey>(), It.IsAny<CancellationToken>()))
            .Callback((TupleKey key, CancellationToken _) => Tuples.RemoveAll(t => Matches(t, key)))
            .Returns(Task.CompletedTask);

        var roles = new Mock<IRoleRepository>();
        roles.Setup(x => x.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) => Roles.FirstOrDefault(r => r.NormalizedName == name));
        roles.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => Roles.FirstOrDefault(r => r.Id == id));
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
}

[ApiController]
[Route("__test/fallback")]
public sealed class FallbackProbeController : ControllerBase
{
  [HttpGet]
  public IActionResult Get() => Ok();
}
