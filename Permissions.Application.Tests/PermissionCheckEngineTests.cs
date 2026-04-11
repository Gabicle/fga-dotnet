using Moq;
using Permissions.Application.DTOs;
using Permissions.Application.Services;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Application.Tests;

public sealed class PermissionCheckEngineTests
{
  private readonly Mock<IRelationTupleRepository> _tupleRepo = new();
  private readonly Mock<IRoleRepository> _roleRepo = new();
  private readonly Mock<IRoleAssignmentRepository> _assignmentRepo = new();
  private readonly PermissionCheckEngine _engine;

  public PermissionCheckEngineTests()
  {
    _engine = new PermissionCheckEngine(
        _tupleRepo.Object,
        _roleRepo.Object,
        _assignmentRepo.Object);
  }

  [Fact]
  public async Task Check_DirectTupleExists_ReturnsAllowed()
  {
    _tupleRepo
        .Setup(r => r.ExistsAsync(It.IsAny<TupleKey>(), default))
        .ReturnsAsync(true);

    var result = await _engine.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    Assert.True(result.Allowed);
    Assert.Equal("Direct tuple match", result.Reason);
  }

  [Fact]
  public async Task Check_NoTupleNoAssignment_ReturnsDenied()
  {
    _tupleRepo
        .Setup(r => r.ExistsAsync(It.IsAny<TupleKey>(), default))
        .ReturnsAsync(false);

    _assignmentRepo
        .Setup(r => r.GetBySubjectAndResourceAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), default))
        .ReturnsAsync([]);

    _assignmentRepo
        .Setup(r => r.GetBySubjectAsync(
            It.IsAny<string>(), It.IsAny<string>(), default))
        .ReturnsAsync([]);

    var result = await _engine.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    Assert.False(result.Allowed);
  }

  [Fact]
  public async Task Check_GlobalRoleMatchesRelation_ReturnsAllowed()
  {
    var viewerRole = Role.Create("viewer");

    var assignment = RoleAssignment.Create(
        viewerRole.Id,
        "user", "7");

    _tupleRepo
        .Setup(r => r.ExistsAsync(It.IsAny<TupleKey>(), default))
        .ReturnsAsync(false);

    _assignmentRepo
        .Setup(r => r.GetBySubjectAndResourceAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), default))
        .ReturnsAsync([]);

    _assignmentRepo
        .Setup(r => r.GetBySubjectAsync("user", "7", default))
        .ReturnsAsync([assignment]);

    _roleRepo
        .Setup(r => r.GetInheritanceChainAsync(viewerRole.Id, default))
        .ReturnsAsync([viewerRole]);

    var result = await _engine.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    Assert.True(result.Allowed);
  }

  [Fact]
  public async Task Check_InheritedRoleGrantsAccess_ReturnsAllowed()
  {
    var viewerRole = Role.Create("viewer");
    var editorRole = Role.Create("editor");
    editorRole.SetParent(viewerRole);

    var assignment = RoleAssignment.Create(
        editorRole.Id,
        "user", "7");

    _tupleRepo
        .Setup(r => r.ExistsAsync(It.IsAny<TupleKey>(), default))
        .ReturnsAsync(false);

    _assignmentRepo
        .Setup(r => r.GetBySubjectAndResourceAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), default))
        .ReturnsAsync([]);

    _assignmentRepo
        .Setup(r => r.GetBySubjectAsync("user", "7", default))
        .ReturnsAsync([assignment]);

    _roleRepo
        .Setup(r => r.GetInheritanceChainAsync(editorRole.Id, default))
        .ReturnsAsync([editorRole, viewerRole]);

    var result = await _engine.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    Assert.True(result.Allowed);
  }

  [Fact]
  public async Task Check_ExpiredAssignment_ReturnsDenied()
  {
    var viewerRole = Role.Create("viewer");

    var assignment = RoleAssignment.Create(
        viewerRole.Id,
        "user", "7",
        expiresAt: DateTime.UtcNow.AddMilliseconds(1));

    await Task.Delay(10);

    _tupleRepo
        .Setup(r => r.ExistsAsync(It.IsAny<TupleKey>(), default))
        .ReturnsAsync(false);

    _assignmentRepo
        .Setup(r => r.GetBySubjectAndResourceAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), default))
        .ReturnsAsync([]);

    _assignmentRepo
        .Setup(r => r.GetBySubjectAsync("user", "7", default))
        .ReturnsAsync([assignment]);

    var result = await _engine.CheckAsync(new CheckPermissionRequest(
        "user", "7", "viewer", "report", "42"));

    Assert.False(result.Allowed);
  }
}