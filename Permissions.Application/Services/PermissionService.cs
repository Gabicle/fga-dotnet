using Permissions.Application.DTOs;
using Permissions.Application.Policies;
using Permissions.Domain.Attributes;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Application.Services;

public sealed class PermissionService
{
  private readonly IRelationTupleRepository _tupleRepository;
  private readonly PermissionCheckEngine _checkEngine;
  private readonly AbacEvaluator _abacEvaluator;

  public PermissionService(
      IRelationTupleRepository tupleRepository,
      PermissionCheckEngine checkEngine,
      AbacEvaluator abacEvaluator)
  {
    _tupleRepository = tupleRepository;
    _checkEngine = checkEngine;
    _abacEvaluator = abacEvaluator;
  }

  public async Task<CheckPermissionResponse> CheckAsync(
      CheckPermissionRequest request,
      CancellationToken cancellationToken = default)
  {
    // Layer 1: Zanzibar tuple expansion
    var tupleResult = await _checkEngine.CheckAsync(request, cancellationToken);
    if (!tupleResult.Allowed)
      return tupleResult;

    // Layer 2: ABAC policy evaluation
    // If no attributes provided, skip ABAC
    if (request.SubjectAttributes is null || request.ResourceAttributes is null)
      return tupleResult;

    var abacResult = _abacEvaluator.Evaluate(
        request.Relation,
        request.SubjectAttributes,
        request.ResourceAttributes,
        request.EnvironmentAttributes ?? new EnvironmentAttributes());

    if (!abacResult.IsAllowed)
      return new CheckPermissionResponse(
          false,
          $"Denied by ABAC policy: {abacResult.DeniedByPolicy}");

    return tupleResult;
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