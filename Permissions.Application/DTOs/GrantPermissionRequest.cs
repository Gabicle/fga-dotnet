namespace Permissions.Application.DTOs;

public sealed record GrantPermissionRequest(
    string SubjectType,
    string SubjectId,
    string Relation,
    string ObjectType,
    string ObjectId,
    string? SubjectRelation = null);