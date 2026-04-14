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
  public async Task CreateAsync_DuplicateName_ThrowsInvalidOperationException()
  {
    var existing = Role.Create("viewer");

    _roleRepo
        .Setup(r => r.GetByNameAsync("VIEWER", default))
        .ReturnsAsync(existing);

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        _service.CreateAsync(new CreateRoleRequest("viewer")));
  }

  [Fact]
  public async Task CreateAsync_WithInvalidParentRoleId_ThrowsInvalidOperationException()
  {
    _roleRepo
        .Setup(r => r.GetByNameAsync("EDITOR", default))
        .ReturnsAsync((Role?)null);

    _roleRepo
        .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
        .ReturnsAsync((Role?)null);

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
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
}