namespace Permissions.Application.DTOs;

public sealed record RoleResponse(
    Guid Id,
    string Name,
    string NormalizedName,
    string? Description,
    Guid? ParentRoleId,
    string? ParentRoleName,
    DateTime CreatedAt);