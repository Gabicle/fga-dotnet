using Permissions.Domain.Attributes;

namespace Permissions.Application.DTOs;

public sealed record CheckPermissionRequest(
    string SubjectType,
    string SubjectId,
    string Relation,
    string ObjectType,
    string ObjectId,
    SubjectAttributes? SubjectAttributes = null,
    ResourceAttributes? ResourceAttributes = null,
    EnvironmentAttributes? EnvironmentAttributes = null);