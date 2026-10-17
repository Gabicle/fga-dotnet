using Permissions.Application.DTOs;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Application.Services;

public sealed class PermissionCheckEngine
{
  private const int MaxExpansionStates = 4096;
  private const int MaxExpansionDepth = 128;
  private const int MaxRoleDescendants = 1024;
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
      CancellationToken cancellationToken,
      bool includeInheritance = true,
      int depth = 0)
  {
    if (depth > MaxExpansionDepth) return false;
    // The mode is part of the state: an explicit userset can still request inheritance
    // after the same role was checked as a descendant without inheritance.
    var key = $"{objectType}:{objectId}#{relation}@{subjectType}:{subjectId}:{includeInheritance}";
    if (!visited.Add(key)) return false;
    if (visited.Count > MaxExpansionStates) return false;

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
          cancellationToken,
          depth: depth + 1);

      if (isRoleMember) return true;
    }

    // A child role inherits its parent's member relation. Validate the complete
    // descendant graph before accepting an inherited match: malformed cycles or
    // oversized graphs fail closed for implicit inheritance only.
    if (includeInheritance && objectType == "role" && relation == "member")
    {
      var role = await _roleRepository.GetByNameAsync(
          objectId.ToUpperInvariant(), cancellationToken);
      if (role is not null)
      {
        var descendants = await GetValidDescendantsAsync(role, cancellationToken);
        if (descendants is not null)
        {
          foreach (var descendant in descendants)
          {
            if (await ExpandAsync("role", descendant.Name, "member", subjectType,
                subjectId, visited, cancellationToken, includeInheritance: false,
                depth: depth + 1))
              return true;
          }
        }
      }
    }

    return false;
  }

  private async Task<IReadOnlyList<Role>?> GetValidDescendantsAsync(
      Role root, CancellationToken cancellationToken)
  {
    var seen = new HashSet<Guid> { root.Id };
    var queue = new Queue<Role>();
    var descendants = new List<Role>();
    queue.Enqueue(root);

    while (queue.Count > 0)
    {
      var parent = queue.Dequeue();
      var children = await _roleRepository.GetChildrenByParentIdAsync(parent.Id, cancellationToken);
      foreach (var child in children)
      {
        if (child.ParentRoleId != parent.Id || !seen.Add(child.Id) ||
            seen.Count > MaxRoleDescendants)
          return null;

        descendants.Add(child);
        queue.Enqueue(child);
      }
    }

    return descendants;
  }
}
