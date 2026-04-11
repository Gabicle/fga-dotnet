namespace Permissions.Application.DTOs;

public sealed record CheckPermissionResponse(
    bool Allowed,
    string Reason);