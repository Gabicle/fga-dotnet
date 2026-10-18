using Moq;
using Permissions.Application.DTOs;
using Permissions.Application.Services;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;

namespace Permissions.Application.Tests;

public sealed class RoleServiceTests
{
  private readonly Mock<IRoleRepository> _roleRepo = new();
  private readonly RoleService _service;

  public RoleServiceTests()
  {
    _roleRepo.Setup(r => r.ExecuteHierarchyMutationAsync(
            It.IsAny<Func<CancellationToken, Task<RoleResponse>>>(), It.IsAny<CancellationToken>()))
        .Returns((Func<CancellationToken, Task<RoleResponse>> operation, CancellationToken token) =>
            operation(token));
    _roleRepo.Setup(r => r.ExecuteHierarchyMutationAsync(
            It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
        .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken token) =>
            operation(token));
    _service = new RoleService(_roleRepo.Object);
  }

  [Fact]
  public async Task CreateAsync_ValidRequest_ReturnsRoleResponse()
  {
    var role = Role.Create("viewer", "Can view resources");

    _roleRepo
        .Setup(r => r.GetByNameAsync("VIEWER", default))
        .ReturnsAsync((Role?)null);

    _roleRepo
        .Setup(r => r.AddAsync(It.IsAny<Role>(), default))
        .Returns(Task.CompletedTask);

    _roleRepo
        .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
        .ReturnsAsync(role);

    var result = await _service.CreateAsync(
        new CreateRoleRequest("viewer", "Can view resources"));

    Assert.Equal("viewer", result.Name);
    Assert.Equal("VIEWER", result.NormalizedName);
    Assert.Equal("Can view resources", result.Description);
  }

  [Fact]
  public async Task CreateAsync_DuplicateName_ThrowsRoleConflictException()
  {
    var existing = Role.Create("viewer");

    _roleRepo
        .Setup(r => r.GetByNameAsync("VIEWER", default))
        .ReturnsAsync(existing);

    await Assert.ThrowsAsync<RoleConflictException>(() =>
        _service.CreateAsync(new CreateRoleRequest("viewer")));
  }

  [Fact]
  public async Task CreateAsync_WithInvalidParentRoleId_ThrowsRoleNotFoundException()
  {
    _roleRepo
        .Setup(r => r.GetByNameAsync("EDITOR", default))
        .ReturnsAsync((Role?)null);

    _roleRepo
        .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
        .ReturnsAsync((Role?)null);

    await Assert.ThrowsAsync<RoleNotFoundException>(() =>
        _service.CreateAsync(new CreateRoleRequest("editor", ParentRoleId: Guid.NewGuid())));
  }

  [Fact]
  public async Task GetAllAsync_ReturnsAllRoles()
  {
    var roles = new List<Role>
        {
            Role.Create("viewer"),
            Role.Create("editor")
        };

    _roleRepo
        .Setup(r => r.GetAllAsync(default))
        .ReturnsAsync(roles);

    var result = await _service.GetAllAsync();

    Assert.Equal(2, result.Count);
  }

  [Fact]
  public async Task GetByIdAsync_ExistingRole_ReturnsRoleResponse()
  {
    var role = Role.Create("viewer");

    _roleRepo
        .Setup(r => r.GetByIdAsync(role.Id, default))
        .ReturnsAsync(role);

    var result = await _service.GetByIdAsync(role.Id);

    Assert.NotNull(result);
    Assert.Equal("viewer", result.Name);
  }

  [Fact]
  public async Task GetByIdAsync_NonExistingRole_ReturnsNull()
  {
    _roleRepo
        .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
        .ReturnsAsync((Role?)null);

    var result = await _service.GetByIdAsync(Guid.NewGuid());

    Assert.Null(result);
  }

  [Fact]
  public async Task AssignParentAsync_SelfParentIsRejectedWithoutUpdate()
  {
    var role = Role.Create("viewer");
    _roleRepo.Setup(r => r.GetByIdAsync(role.Id, default)).ReturnsAsync(role);
    _roleRepo.Setup(r => r.GetInheritanceChainAsync(role.Id, default))
        .ReturnsAsync([role]);

    await Assert.ThrowsAsync<RoleConflictException>(() =>
        _service.AssignParentAsync(role.Id, role.Id));
    Assert.Null(role.ParentRoleId);
    _roleRepo.Verify(r => r.UpdateAsync(It.IsAny<Role>(), default), Times.Never);
  }

  [Fact]
  public async Task AssignParentAsync_IndirectCycleIsRejectedAndParentPreserved()
  {
    var viewer = Role.Create("viewer");
    var editor = Role.Create("editor");
    var admin = Role.Create("admin");
    editor.SetParent(viewer);
    admin.SetParent(editor);
    _roleRepo.Setup(r => r.GetByIdAsync(viewer.Id, default)).ReturnsAsync(viewer);
    _roleRepo.Setup(r => r.GetByIdAsync(admin.Id, default)).ReturnsAsync(admin);
    _roleRepo.Setup(r => r.GetInheritanceChainAsync(admin.Id, default))
        .ReturnsAsync([admin, editor, viewer]);

    await Assert.ThrowsAsync<RoleConflictException>(() =>
        _service.AssignParentAsync(viewer.Id, admin.Id));
    Assert.Null(viewer.ParentRoleId);
    Assert.Equal(viewer.Id, editor.ParentRoleId);
    _roleRepo.Verify(r => r.UpdateAsync(It.IsAny<Role>(), default), Times.Never);
  }

  [Fact]
  public async Task AssignParentAsync_ExistingHierarchyCycleIsRejectedWithoutUpdate()
  {
    var viewer = Role.Create("viewer");
    var editor = Role.Create("editor");
    _roleRepo.Setup(r => r.GetByIdAsync(viewer.Id, default)).ReturnsAsync(viewer);
    _roleRepo.Setup(r => r.GetByIdAsync(editor.Id, default)).ReturnsAsync(editor);
    _roleRepo.Setup(r => r.GetInheritanceChainAsync(editor.Id, default))
        .ThrowsAsync(new InvalidOperationException("Role hierarchy contains a cycle."));

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        _service.AssignParentAsync(viewer.Id, editor.Id));
    Assert.Null(viewer.ParentRoleId);
    _roleRepo.Verify(r => r.UpdateAsync(It.IsAny<Role>(), default), Times.Never);
  }
}
