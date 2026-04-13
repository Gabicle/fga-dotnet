using Permissions.Application.DTOs;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Application.Services;

public sealed class PermissionCheckEngine
{
  private readonly IRelationTupleRepository _tupleRepository;
  private readonly IRoleRepository _roleRepository;

  public PermissionCheckEngine(
      IRelationTupleRepository tupleRepository,
      IRoleRepository roleRepository)
  {
    _tupleRepository = tupleRepository;
    _roleRepository = roleRepository;
  }

  public async Task<CheckPermissionResponse> CheckAsync(
      CheckPermissionRequest request,
      CancellationToken cancellationToken = default)
  {
    var visited = new HashSet<string>();
    var allowed = await ExpandAsync(
        request.ObjectType,
        request.ObjectId,
        request.Relation,
        request.SubjectType,
        request.SubjectId,
        visited,
        cancellationToken);

    return allowed
        ? new CheckPermissionResponse(true, "Allowed via tuple expansion")
        : new CheckPermissionResponse(false, "No matching tuple found");
  }

  private async Task<bool> ExpandAsync(
      string objectType,
      string objectId,
      string relation,
      string subjectType,
      string subjectId,
      HashSet<string> visited,
      CancellationToken cancellationToken)
  {
    // Guard against infinite loops in circular role definitions
    var key = $"{objectType}:{objectId}#{relation}@{subjectType}:{subjectId}";
    if (!visited.Add(key)) return false;

    // Step 1: direct tuple match
    var directKey = new TupleKey(objectType, objectId, relation, subjectType, subjectId);
    if (await _tupleRepository.ExistsAsync(directKey, cancellationToken))
      return true;

    // Step 2: expand tuples where subject is a role member
    // e.g. report:42#viewer@role:editor#member
    // means "anyone who is a member of role:editor can view report:42"
    var indirectTuples = await _tupleRepository.GetByObjectAndRelationAsync(
        objectType, objectId, relation, cancellationToken);

    foreach (var tuple in indirectTuples.Where(t => t.SubjectRelation is not null))
    {
      // tuple.SubjectType = "role", tuple.SubjectId = "editor", tuple.SubjectRelation = "member"
      // check if subjectType:subjectId has tuple.SubjectRelation on tuple.SubjectType:tuple.SubjectId
      var isRoleMember = await ExpandAsync(
          tuple.SubjectType,
          tuple.SubjectId,
          tuple.SubjectRelation!,
          subjectType,
          subjectId,
          visited,
          cancellationToken);

      if (isRoleMember) return true;
    }

    // Step 3: walk role inheritance chain
    // if role:editor#member@user:7 and editor's parent is viewer
    // then user:7 inherits viewer permissions
    if (objectType == "role")
    {
      var role = await _roleRepository.GetByNameAsync(
          objectId.ToUpperInvariant(), cancellationToken);

      if (role?.ParentRoleId is not null)
      {
        var parentRole = await _roleRepository.GetByIdAsync(
            role.ParentRoleId.Value, cancellationToken);

        if (parentRole is not null)
        {
          var inheritedAccess = await ExpandAsync(
              objectType,
              parentRole.Name,
              relation,
              subjectType,
              subjectId,
              visited,
              cancellationToken);

          if (inheritedAccess) return true;
        }
      }
    }

    return false;
  }
}