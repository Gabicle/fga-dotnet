using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Permissions.Api.Security;
using Permissions.Application.DTOs;
using Permissions.Application.Services;
using Permissions.Domain.ValueObjects;

namespace Permissions.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PermissionsController : ControllerBase
{
  private static readonly EventId DecisionEvent = new(1001, "AuthorizationDecision");
  private readonly PermissionService _permissionService;
  private readonly ILogger<PermissionsController> _logger;

  public PermissionsController(PermissionService permissionService, ILogger<PermissionsController> logger)
  {
    _permissionService = permissionService;
    _logger = logger;
  }

  [HttpPost("check")]
  [Authorize(Policy = ApiKeyPolicies.Check)]
  [ProducesResponseType(typeof(CheckPermissionResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Check(
      [FromBody] CheckPermissionRequest request,
      CancellationToken cancellationToken)
  {
    var started = Stopwatch.GetTimestamp();
    var decisionId = Guid.NewGuid().ToString("N");
    var reasonCode = AuthorizationReasonCodes.EvaluationError;
    var outcome = "error";
    var httpStatus = StatusCodes.Status500InternalServerError;
    try
    {
      Response.Headers["X-Authorization-Decision-Id"] = decisionId;
      var result = await _permissionService.CheckAsync(request, cancellationToken);
      reasonCode = result.ReasonCode;
      outcome = result.Allowed ? "allow" : "deny";
      httpStatus = StatusCodes.Status200OK;
      return Ok(result);
    }
    catch (InvalidAuthorizationContextException)
    {
      reasonCode = AuthorizationReasonCodes.InvalidContext;
      httpStatus = StatusCodes.Status400BadRequest;
      return BadRequest(new { error = "Invalid authorization context.", reasonCode });
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // The client has disconnected; no final HTTP response can be guaranteed.
      httpStatus = 0;
      throw;
    }
    catch (Exception)
    {
      reasonCode = AuthorizationReasonCodes.EvaluationError;
      outcome = "error";
      httpStatus = StatusCodes.Status500InternalServerError;
      return StatusCode(httpStatus, new { error = "Authorization evaluation failed.", reasonCode });
    }
    finally
    {
      _logger.LogInformation(DecisionEvent,
          "Authorization decision {DecisionId} {ServiceIdentity} {SubjectType} {SubjectId} {ObjectType} {ObjectId} {Relation} {Outcome} {ReasonCode} {DurationMs} {HttpStatus}",
          decisionId,
          User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown-authenticated",
          request.SubjectType, request.SubjectId, request.ObjectType, request.ObjectId, request.Relation,
          outcome, reasonCode,
          Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3), httpStatus);
    }
  }

  [HttpPost("grant")]
  [Authorize(Policy = ApiKeyPolicies.Admin)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<IActionResult> Grant(
      [FromBody] GrantPermissionRequest request,
      CancellationToken cancellationToken)
  {
    try
    {
      await _permissionService.GrantAsync(request, cancellationToken);
      return NoContent();
    }
    catch (ArgumentException ex)
    {
      return BadRequest(new { error = ex.Message });
    }
    catch (TupleGrantConflictException ex)
    {
      return Conflict(new { error = ex.Message });
    }
  }

  [HttpPost("revoke")]
  [Authorize(Policy = ApiKeyPolicies.Admin)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Revoke(
      [FromBody] RevokePermissionRequest request,
      CancellationToken cancellationToken)
  {
    try
    {
      var key = new TupleKey(
          request.ObjectType,
          request.ObjectId,
          request.Relation,
          request.SubjectType,
          request.SubjectId,
          request.SubjectRelation);

      await _permissionService.RevokeAsync(key, cancellationToken);
      return NoContent();
    }
    catch (ArgumentException ex)
    {
      return BadRequest(new { error = ex.Message });
    }
  }
}
