namespace Permissions.Application.DTOs;

public sealed record CheckPermissionRequest(
    string SubjectType,
    string SubjectId,
    string Relation,
    string ObjectType,
    string ObjectId);