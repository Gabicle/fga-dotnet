namespace Permissions.Application.DTOs;

public sealed record CreateRoleRequest(
    string Name,
    string? Description = null,
    Guid? ParentRoleId = null);