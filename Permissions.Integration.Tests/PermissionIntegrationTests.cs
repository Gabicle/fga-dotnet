using Permissions.Application.DTOs;
using Permissions.Domain.Attributes;

namespace Permissions.Integration.Tests;

public sealed class PermissionIntegrationTests : IntegrationTestBase
{
  public PermissionIntegrationTests(DatabaseFixture fixture) : base(fixture) { }

  [Fact]
  public async Task Grant_ThenCheck_DirectTuple_ReturnsAllowed()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    var result = await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42"));

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

    var result = await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    Assert.False(result.Allowed);
  }

  [Fact]
  public async Task Grant_DuplicateTuple_DoesNotThrow()
  {
    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    await PermissionService.GrantAsync(new GrantPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    var result = await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    Assert.True(result.Allowed);
  }

  [Fact]
  public async Task Check_NoTuple_ReturnsDenied()
  {
    var result = await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    Assert.False(result.Allowed);
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

    var result = await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42"));

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
    var result = await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "99", "viewer", "report", "42"));

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
          Region = "eu-west"
        },
        ResourceAttributes: new ResourceAttributes
        {
          ResourceType = "report",
          ResourceId = "42",
          Region = "eu-west"
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
          Region = "us-east"
        },
        ResourceAttributes: new ResourceAttributes
        {
          ResourceType = "report",
          ResourceId = "42",
          Region = "eu-west"
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
          Region = "eu-west"
        },
        ResourceAttributes: new ResourceAttributes
        {
          ResourceType = "report",
          ResourceId = "42",
          Region = "eu-west",
          Status = "locked"
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

    var result = await PermissionService.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    Assert.False(result.Allowed);
  }
}