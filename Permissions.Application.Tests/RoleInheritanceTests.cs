using Moq;
using Permissions.Application.DTOs;
using Permissions.Application.Services;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Application.Tests;

public sealed class RoleInheritanceTests
{
  private readonly List<Role> _roles = [];
  private readonly List<RelationTuple> _tuples = [];
  private readonly PermissionCheckEngine _engine;

  public RoleInheritanceTests()
  {
    var tupleRepository = new Mock<IRelationTupleRepository>();
    tupleRepository.Setup(r => r.ExistsAsync(It.IsAny<TupleKey>(), default))
        .ReturnsAsync((TupleKey key, CancellationToken _) => _tuples.Any(t =>
            t.ObjectType == key.ObjectType && t.ObjectId == key.ObjectId &&
            t.Relation == key.Relation && t.SubjectType == key.SubjectType &&
            t.SubjectId == key.SubjectId && t.SubjectRelation == key.SubjectRelation &&
            !t.IsExpired()));
    tupleRepository.Setup(r => r.GetByObjectAndRelationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default))
        .ReturnsAsync((string type, string id, string relation, CancellationToken _) =>
            (IReadOnlyList<RelationTuple>)_tuples.Where(t =>
                t.ObjectType == type && t.ObjectId == id && t.Relation == relation &&
                !t.IsExpired()).ToList());

    var roleRepository = new Mock<IRoleRepository>();
    roleRepository.Setup(r => r.GetByNameAsync(It.IsAny<string>(), default))
        .ReturnsAsync((string name, CancellationToken _) =>
            _roles.SingleOrDefault(r => r.NormalizedName == name));
    roleRepository.Setup(r => r.GetChildrenByParentIdAsync(It.IsAny<Guid>(), default))
        .ReturnsAsync((Guid parentId, CancellationToken _) =>
            (IReadOnlyList<Role>)_roles.Where(r => r.ParentRoleId == parentId).ToList());

    _engine = new PermissionCheckEngine(tupleRepository.Object, roleRepository.Object);
  }

  private Role AddRole(string name, Role? parent = null)
  {
    var role = Role.Create(name);
    if (parent is not null) role.SetParent(parent);
    _roles.Add(role);
    return role;
  }

  private void Grant(string objectType, string objectId, string relation,
      string subjectType, string subjectId, string? subjectRelation = null)
      => _tuples.Add(RelationTuple.Create(objectType, objectId, relation,
          subjectType, subjectId, subjectRelation));

  private Task<CheckPermissionResponse> Check(string user, string relation = "viewer",
      string objectId = "42") => _engine.CheckAsync(new CheckPermissionRequest(
          "user", user, relation, "report", objectId));

  [Fact]
  public async Task DirectMembershipAndUnrelatedUser()
  {
    Grant("report", "42", "viewer", "role", "viewer", "member");
    Grant("role", "viewer", "member", "user", "7");
    Assert.True((await Check("7")).Allowed);
    Assert.False((await Check("99")).Allowed);
  }

  [Fact]
  public async Task EditorInheritsViewerButViewerDoesNotInheritEditor()
  {
    var viewer = AddRole("viewer");
    AddRole("editor", viewer);
    Grant("role", "editor", "member", "user", "7");
    Grant("role", "viewer", "member", "user", "8");
    Grant("report", "42", "viewer", "role", "viewer", "member");
    Grant("report", "42", "editor", "role", "editor", "member");

    Assert.True((await Check("7")).Allowed);
    Assert.False((await Check("8", "editor")).Allowed);
  }

  [Fact]
  public async Task AdminInheritsAcrossTwoLevelsAndSiblingRoles()
  {
    var viewer = AddRole("viewer");
    var editor = AddRole("editor", viewer);
    AddRole("admin", editor);
    AddRole("auditor", viewer);
    Grant("role", "admin", "member", "user", "7");
    Grant("role", "auditor", "member", "user", "8");
    Grant("report", "42", "viewer", "role", "viewer", "member");
    Grant("report", "42", "editor", "role", "editor", "member");

    Assert.True((await Check("7")).Allowed);
    Assert.True((await Check("8")).Allowed);
    Assert.True((await Check("7", "editor")).Allowed);
    Assert.False((await Check("8", "editor")).Allowed);
  }

  [Fact]
  public async Task GroupUsersetThroughChildRoleStillExpands()
  {
    var viewer = AddRole("viewer");
    AddRole("editor", viewer);
    Grant("report", "42", "viewer", "role", "viewer", "member");
    Grant("role", "editor", "member", "group", "research", "member");
    Grant("group", "research", "member", "user", "7");

    Assert.True((await Check("7")).Allowed);
    Assert.False((await Check("8")).Allowed);
  }

  [Fact]
  public async Task DirectAndGenericIndirectTuplesDoNotRequireRoleMetadata()
  {
    Grant("report", "42", "viewer", "user", "7");
    Grant("report", "42", "viewer", "group", "research", "member");
    Grant("group", "research", "member", "user", "8");
    Grant("report", "43", "viewer", "role", "missing", "member");
    Grant("role", "missing", "member", "user", "9");

    Assert.True((await Check("7")).Allowed);
    Assert.True((await Check("8")).Allowed);
    Assert.True((await Check("9", objectId: "43")).Allowed);
    Assert.False((await Check("10", objectId: "43")).Allowed);
  }

  [Fact]
  public async Task OnlyMemberRelationIsInherited()
  {
    var viewer = AddRole("viewer");
    AddRole("editor", viewer);
    Grant("report", "42", "viewer", "role", "viewer", "owner");
    Grant("role", "editor", "owner", "user", "7");

    Assert.False((await Check("7")).Allowed);
  }

  [Fact]
  public async Task MalformedCycleDoesNotGrantViaInheritanceButDirectTupleRemainsValid()
  {
    var viewer = AddRole("viewer");
    var editor = AddRole("editor", viewer);
    viewer.SetParent(editor);
    Grant("report", "42", "viewer", "role", "viewer", "member");
    Grant("role", "editor", "member", "user", "7");
    Grant("role", "viewer", "member", "user", "8");
    Grant("report", "42", "viewer", "user", "9");

    Assert.False((await Check("7")).Allowed);
    Assert.True((await Check("8")).Allowed);
    Assert.True((await Check("9")).Allowed);
  }

  [Fact]
  public async Task ReparentingAddsAndRemovesInheritedPermission()
  {
    var viewer = AddRole("viewer");
    var editor = AddRole("editor");
    var auditor = AddRole("auditor", editor);
    Grant("report", "42", "viewer", "role", "viewer", "member");
    Grant("role", "auditor", "member", "user", "7");

    Assert.False((await Check("7")).Allowed);
    auditor.SetParent(viewer);
    Assert.True((await Check("7")).Allowed);
    auditor.SetParent(editor);
    Assert.False((await Check("7")).Allowed);
  }

  [Fact]
  public async Task MixedCaseRoleNamesUseNormalizedLookupAndStoredTupleIdentifier()
  {
    var viewer = AddRole("Viewer");
    AddRole("Editor", viewer);
    Grant("report", "42", "viewer", "role", "Viewer", "member");
    Grant("role", "Editor", "member", "user", "7");

    Assert.True((await Check("7")).Allowed);
  }

  [Fact]
  public async Task ExpiredChildMembershipDoesNotInherit()
  {
    var viewer = AddRole("viewer");
    AddRole("editor", viewer);
    Grant("report", "42", "viewer", "role", "viewer", "member");
    _tuples.Add(RelationTuple.Create("role", "editor", "member", "user", "7",
        expiresAt: DateTime.UtcNow.AddMilliseconds(5)));
    await Task.Delay(20);

    Assert.False((await Check("7")).Allowed);
  }

  [Fact]
  public async Task OversizedDescendantGraphDoesNotReturnInheritedAllow()
  {
    var parent = AddRole("role-0");
    Grant("report", "42", "viewer", "role", parent.Name, "member");
    for (var index = 1; index <= 1024; index++)
      parent = AddRole($"role-{index}", parent);
    Grant("role", "role-1", "member", "user", "7");

    Assert.False((await Check("7")).Allowed);
    Grant("report", "42", "viewer", "user", "8");
    Assert.True((await Check("8")).Allowed);
  }

  [Fact]
  public async Task ExcessiveIndirectRecursionDoesNotReturnAllow()
  {
    Grant("report", "42", "viewer", "group", "group-0", "member");
    for (var index = 0; index < 130; index++)
      Grant("group", $"group-{index}", "member", "group", $"group-{index + 1}", "member");
    Grant("group", "group-130", "member", "user", "7");

    Assert.False((await Check("7")).Allowed);
  }
}
