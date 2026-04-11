using Permissions.Application.DTOs;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Application.Services;

public sealed class PermissionCheckEngine
{
  private readonly IRelationTupleRepository _tupleRepository;
  private readonly IRoleRepository _roleRepository;
  private readonly IRoleAssignmentRepository _roleAssignmentRepository;

  public PermissionCheckEngine(
      IRelationTupleRepository tupleRepository,
      IRoleRepository roleRepository,
      IRoleAssignmentRepository roleAssignmentRepository)
  {
    _tupleRepository = tupleRepository;
    _roleRepository = roleRepository;
    _roleAssignmentRepository = roleAssignmentRepository;
  }

  public async Task<CheckPermissionResponse> CheckAsync(
      CheckPermissionRequest request,
      CancellationToken cancellationToken = default)
  {
    // Step 1: direct tuple check
    var key = new TupleKey(
        request.ObjectType,
        request.ObjectId,
        request.Relation,
        request.SubjectType,
        request.SubjectId);

    var directMatch = await _tupleRepository.ExistsAsync(key, cancellationToken);
    if (directMatch)
      return new CheckPermissionResponse(true, "Direct tuple match");

    // Step 2: check role assignments scoped to this resource
    var scopedAssignments = await _roleAssignmentRepository.GetBySubjectAndResourceAsync(
        request.SubjectType,
        request.SubjectId,
        request.ObjectType,
        request.ObjectId,
        cancellationToken);

    foreach (var assignment in scopedAssignments.Where(a => !a.IsExpired()))
    {
      var chain = await _roleRepository.GetInheritanceChainAsync(
          assignment.RoleId,
          cancellationToken);

      if (chain.Any(r => r.Name.Equals(request.Relation, StringComparison.OrdinalIgnoreCase)))
        return new CheckPermissionResponse(true, $"Scoped role match via {assignment.Role.Name}");
    }

    // Step 3: check global role assignments
    var globalAssignments = await _roleAssignmentRepository.GetBySubjectAsync(
        request.SubjectType,
        request.SubjectId,
        cancellationToken);

    foreach (var assignment in globalAssignments.Where(a => a.IsGlobal() && !a.IsExpired()))
    {
      var chain = await _roleRepository.GetInheritanceChainAsync(
          assignment.RoleId,
          cancellationToken);

      if (chain.Any(r => r.Name.Equals(request.Relation, StringComparison.OrdinalIgnoreCase)))
        return new CheckPermissionResponse(true, $"Global role match via {assignment.Role.Name}");
    }

    return new CheckPermissionResponse(false, "No matching tuple or role assignment found");
  }
}