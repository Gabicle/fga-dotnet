using Microsoft.AspNetCore.Mvc;
using Permissions.Application.DTOs;
using Permissions.Application.Services;
using Permissions.Domain.ValueObjects;

namespace Permissions.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PermissionsController : ControllerBase
{
  private readonly PermissionService _permissionService;

  public PermissionsController(PermissionService permissionService)
  {
    _permissionService = permissionService;
  }

  [HttpPost("check")]
  [ProducesResponseType(typeof(CheckPermissionResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Check(
      [FromBody] CheckPermissionRequest request,
      CancellationToken cancellationToken)
  {
    var result = await _permissionService.CheckAsync(request, cancellationToken);
    return Ok(result);
  }

  [HttpPost("grant")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Grant(
      [FromBody] GrantPermissionRequest request,
      CancellationToken cancellationToken)
  {
    await _permissionService.GrantAsync(request, cancellationToken);
    return NoContent();
  }

  [HttpPost("revoke")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> Revoke(
      [FromBody] RevokePermissionRequest request,
      CancellationToken cancellationToken)
  {
    var key = new TupleKey(
        request.ObjectType,
        request.ObjectId,
        request.Relation,
        request.SubjectType,
        request.SubjectId);

    await _permissionService.RevokeAsync(key, cancellationToken);
    return NoContent();
  }
}