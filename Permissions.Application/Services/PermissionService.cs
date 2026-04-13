using Permissions.Application.DTOs;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Application.Services;

public sealed class PermissionService
{
  private readonly IRelationTupleRepository _tupleRepository;
  private readonly PermissionCheckEngine _checkEngine;

  public PermissionService(
      IRelationTupleRepository tupleRepository,
      PermissionCheckEngine checkEngine)
  {
    _tupleRepository = tupleRepository;
    _checkEngine = checkEngine;
  }

  public async Task<CheckPermissionResponse> CheckAsync(
      CheckPermissionRequest request,
      CancellationToken cancellationToken = default)
  {
    return await _checkEngine.CheckAsync(request, cancellationToken);
  }

  public async Task GrantAsync(
     GrantPermissionRequest request,
     CancellationToken cancellationToken = default)
  {
    var key = new TupleKey(
        request.ObjectType,
        request.ObjectId,
        request.Relation,
        request.SubjectType,
        request.SubjectId,
        request.SubjectRelation);

    var exists = await _tupleRepository.ExistsAsync(key, cancellationToken);
    if (exists) return;

    var tuple = RelationTuple.Create(
        request.ObjectType,
        request.ObjectId,
        request.Relation,
        request.SubjectType,
        request.SubjectId,
        request.SubjectRelation);

    await _tupleRepository.AddAsync(tuple, cancellationToken);
  }

  public async Task RevokeAsync(
      TupleKey key,
      CancellationToken cancellationToken = default)
  {
    await _tupleRepository.DeleteAsync(key, cancellationToken);
  }
}