namespace Permissions.Application.DTOs;

public sealed record RevokePermissionRequest(
    string SubjectType,
    string SubjectId,
    string Relation,
    string ObjectType,
    string ObjectId);