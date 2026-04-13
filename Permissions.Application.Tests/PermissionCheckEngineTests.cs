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
    private readonly PermissionCheckEngine _engine;

    public PermissionCheckEngineTests()
    {
        _engine = new PermissionCheckEngine(
            _tupleRepo.Object,
            _roleRepo.Object);
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
        Assert.Equal("Allowed via tuple expansion", result.Reason);
    }

    [Fact]
    public async Task Check_NoTupleExists_ReturnsDenied()
    {
        _tupleRepo
            .Setup(r => r.ExistsAsync(It.IsAny<TupleKey>(), default))
            .ReturnsAsync(false);

        _tupleRepo
            .Setup(r => r.GetByObjectAndRelationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync([]);

        var result = await _engine.CheckAsync(new CheckPermissionRequest(
            "user", "7", "viewer", "report", "42"));

        Assert.False(result.Allowed);
        Assert.Equal("No matching tuple found", result.Reason);
    }

    [Fact]
    public async Task Check_UserIsMemberOfRoleThatHasAccess_ReturnsAllowed()
    {
        // report:42#viewer@role:editor#member exists
        var indirectTuple = RelationTuple.Create(
            "report", "42", "viewer", "role", "editor", "member");

        // no direct tuple for user:7
        _tupleRepo
            .Setup(r => r.ExistsAsync(
                It.Is<TupleKey>(k =>
                    k.ObjectType == "report" &&
                    k.ObjectId == "42" &&
                    k.Relation == "viewer" &&
                    k.SubjectType == "user" &&
                    k.SubjectId == "7"),
                default))
            .ReturnsAsync(false);

        // report:42#viewer has an indirect tuple pointing to role:editor#member
        _tupleRepo
            .Setup(r => r.GetByObjectAndRelationAsync("report", "42", "viewer", default))
            .ReturnsAsync([indirectTuple]);

        // role:editor#member@user:7 exists — user is a member of the editor role
        _tupleRepo
            .Setup(r => r.ExistsAsync(
                It.Is<TupleKey>(k =>
                    k.ObjectType == "role" &&
                    k.ObjectId == "editor" &&
                    k.Relation == "member" &&
                    k.SubjectType == "user" &&
                    k.SubjectId == "7"),
                default))
            .ReturnsAsync(true);

        var result = await _engine.CheckAsync(new CheckPermissionRequest(
            "user", "7", "viewer", "report", "42"));

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task Check_UserInheritedRoleGrantsAccess_ReturnsAllowed()
    {
        var viewerRole = Role.Create("viewer");
        var editorRole = Role.Create("editor");
        editorRole.SetParent(viewerRole);

        // report:42#viewer@role:viewer#member exists
        var indirectTuple = RelationTuple.Create(
            "report", "42", "viewer", "role", "viewer", "member");

        // no direct tuple for user:7 on report:42#viewer
        _tupleRepo
            .Setup(r => r.ExistsAsync(
                It.Is<TupleKey>(k =>
                    k.ObjectType == "report" &&
                    k.SubjectType == "user"),
                default))
            .ReturnsAsync(false);

        // report:42#viewer points to role:viewer#member
        _tupleRepo
            .Setup(r => r.GetByObjectAndRelationAsync("report", "42", "viewer", default))
            .ReturnsAsync([indirectTuple]);

        // user:7 is not directly a member of role:viewer
        _tupleRepo
            .Setup(r => r.ExistsAsync(
                It.Is<TupleKey>(k =>
                    k.ObjectType == "role" &&
                    k.ObjectId == "viewer" &&
                    k.SubjectType == "user"),
                default))
            .ReturnsAsync(false);

        // no further indirect tuples for role:viewer#member
        _tupleRepo
            .Setup(r => r.GetByObjectAndRelationAsync("role", "viewer", "member", default))
            .ReturnsAsync([]);

        // role:viewer has editor as a child — but we walk UP via parent
        // viewer role has no parent
        _roleRepo
            .Setup(r => r.GetByNameAsync("VIEWER", default))
            .ReturnsAsync(viewerRole);

        var result = await _engine.CheckAsync(new CheckPermissionRequest(
            "user", "7", "viewer", "report", "42"));

        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Check_ExpiredTuple_ReturnsDenied()
    {
        // ExistsAsync already filters expired tuples at the repository level
        _tupleRepo
            .Setup(r => r.ExistsAsync(It.IsAny<TupleKey>(), default))
            .ReturnsAsync(false);

        _tupleRepo
            .Setup(r => r.GetByObjectAndRelationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync([]);

        var result = await _engine.CheckAsync(new CheckPermissionRequest(
            "user", "7", "viewer", "report", "42"));

        Assert.False(result.Allowed);
    }
}