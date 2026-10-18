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
    var requirement = AbacRequirements.For(request.ObjectType, request.Relation);
    ValidateAttributeIdentities(request, requirement is not null);
    if (requirement is not null)
      ValidateRequiredAttributes(request, requirement);

    // Layer 1: Zanzibar tuple expansion
    var tupleResult = await _checkEngine.CheckAsync(request, cancellationToken);
    if (!tupleResult.Allowed)
      return tupleResult;

    // Operations without a server-owned requirement remain ReBAC-only.
    if (requirement is null)
      return tupleResult;

    var abacResult = _abacEvaluator.Evaluate(
        request.ObjectType,
        request.Relation,
        request.SubjectAttributes!,
        request.ResourceAttributes!,
        request.EnvironmentAttributes ?? new EnvironmentAttributes(),
        requirement);

    if (!abacResult.IsApplicable)
      throw new InvalidOperationException("Required ABAC policy is unavailable.");

    if (!abacResult.IsAllowed)
      return new CheckPermissionResponse(
          false,
          $"Denied by ABAC policy: {abacResult.DeniedByPolicy}");

    return tupleResult;
  }

  private static void ValidateAttributeIdentities(
      CheckPermissionRequest request, bool identitiesRequired)
  {
    if (request.SubjectAttributes is { } subject)
    {
      ValidateIdentity(subject.SubjectType, request.SubjectType, identitiesRequired);
      ValidateIdentity(subject.SubjectId, request.SubjectId, identitiesRequired);
    }

    if (request.ResourceAttributes is { } resource)
    {
      ValidateIdentity(resource.ResourceType, request.ObjectType, identitiesRequired);
      ValidateIdentity(resource.ResourceId, request.ObjectId, identitiesRequired);
    }
  }

  private static void ValidateIdentity(string? supplied, string expected, bool required)
  {
    if (string.IsNullOrWhiteSpace(supplied))
    {
      if (required)
        throw new ArgumentException("Required ABAC identity is missing.");
      return;
    }

    if (!string.Equals(supplied, expected, StringComparison.Ordinal))
      throw new ArgumentException("ABAC identity does not match the checked identity.");
  }

  private static void ValidateRequiredAttributes(
      CheckPermissionRequest request, AbacRequirements.Requirement requirement)
  {
    var subject = request.SubjectAttributes;
    var resource = request.ResourceAttributes;
    if (subject is null || resource is null)
      throw new ArgumentException("Required ABAC attribute objects are missing.");

    if (string.IsNullOrWhiteSpace(subject.Region) ||
        string.IsNullOrWhiteSpace(resource.Region) ||
        !subject.ClearanceLevel.HasValue || !resource.SensitivityLevel.HasValue)
      throw new ArgumentException("Required ABAC policy attributes are missing.");

    if (requirement.RequiresStatus &&
        (string.IsNullOrWhiteSpace(resource.Status) ||
         !AbacRequirements.IsSupportedReportStatus(resource.Status)))
      throw new ArgumentException("Resource status is missing or unsupported for this operation.");
  }

  public async Task GrantAsync(
    GrantPermissionRequest request,
    CancellationToken cancellationToken = default)
  {
    var tuple = RelationTuple.Create(
        request.ObjectType,
        request.ObjectId,
        request.Relation,
        request.SubjectType,
        request.SubjectId,
        request.SubjectRelation,
        request.ExpiresAt);

    var outcome = await _tupleRepository.GrantAsync(tuple, cancellationToken);
    switch (outcome)
    {
      case TupleGrantOutcome.Success:
        return;
      case TupleGrantOutcome.ActiveExpirationConflict:
        throw new TupleGrantConflictException();
      case TupleGrantOutcome.ExpirationElapsed:
        throw new ArgumentException("ExpiresAt must be a future UTC timestamp.", nameof(request));
      default:
        throw new InvalidOperationException("Unexpected tuple grant result.");
    }
  }

  public async Task RevokeAsync(
      TupleKey key,
      CancellationToken cancellationToken = default)
  {
    await _tupleRepository.DeleteAsync(key, cancellationToken);
  }
}
