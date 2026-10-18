using Permissions.Application.DTOs;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;

namespace Permissions.Application.Services;

public sealed class RoleService
{
  private readonly IRoleRepository _roleRepository;

  public RoleService(IRoleRepository roleRepository)
  {
    _roleRepository = roleRepository;
  }

  public async Task<RoleResponse> CreateAsync(
     CreateRoleRequest request,
     CancellationToken cancellationToken = default)
  {
    if (request.ParentRoleId.HasValue)
      return await _roleRepository.ExecuteHierarchyMutationAsync(
          token => CreateCoreAsync(request, token), cancellationToken);

    return await CreateCoreAsync(request, cancellationToken);
  }

  private async Task<RoleResponse> CreateCoreAsync(
      CreateRoleRequest request, CancellationToken cancellationToken)
  {
    var existing = await _roleRepository.GetByNameAsync(
        request.Name.ToUpperInvariant(), cancellationToken);

    if (existing is not null)
      throw new RoleConflictException();

    var role = Role.Create(request.Name, request.Description);

    if (request.ParentRoleId.HasValue)
    {
      var parentRole = await _roleRepository.GetByIdAsync(
          request.ParentRoleId.Value, cancellationToken);

      if (parentRole is null)
        throw new RoleNotFoundException();

      await _roleRepository.GetInheritanceChainAsync(parentRole.Id, cancellationToken);
      role.SetParentId(parentRole.Id);
    }

    await _roleRepository.AddAsync(role, cancellationToken);

    var saved = await _roleRepository.GetByIdAsync(role.Id, cancellationToken);
    return ToResponse(saved!);
  }

  public async Task<IReadOnlyList<RoleResponse>> GetAllAsync(
      CancellationToken cancellationToken = default)
  {
    var roles = await _roleRepository.GetAllAsync(cancellationToken);
    return roles.Select(ToResponse).ToList().AsReadOnly();
  }

  public async Task<RoleResponse?> GetByIdAsync(
      Guid id,
      CancellationToken cancellationToken = default)
  {
    var role = await _roleRepository.GetByIdAsync(id, cancellationToken);
    return role is null ? null : ToResponse(role);
  }

  public async Task AssignParentAsync(
     Guid roleId,
     Guid parentRoleId,
     CancellationToken cancellationToken = default)
  {
    await _roleRepository.ExecuteHierarchyMutationAsync(async token =>
    {
      await AssignParentCoreAsync(roleId, parentRoleId, token);
      return true;
    }, cancellationToken);
  }

  private async Task AssignParentCoreAsync(
      Guid roleId, Guid parentRoleId, CancellationToken cancellationToken)
  {
    var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken)
        ?? throw new RoleNotFoundException();

    var parentRole = await _roleRepository.GetByIdAsync(parentRoleId, cancellationToken)
        ?? throw new RoleNotFoundException();

    if (role.Id == parentRole.Id)
      throw new RoleConflictException();

    var parentChain = await _roleRepository.GetInheritanceChainAsync(
        parentRole.Id, cancellationToken);
    if (parentChain.Any(ancestor => ancestor.Id == role.Id))
      throw new RoleConflictException();

    role.SetParent(parentRole);
    await _roleRepository.UpdateAsync(role, cancellationToken);
  }

  private static RoleResponse ToResponse(Role role) => new(
      role.Id,
      role.Name,
      role.NormalizedName,
      role.Description,
      role.ParentRoleId,
      role.ParentRole?.Name,
      role.CreatedAt);
}
