using Microsoft.AspNetCore.Mvc;
using Permissions.Application.DTOs;
using Permissions.Application.Services;

namespace Permissions.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class RolesController : ControllerBase
{
  private readonly RoleService _roleService;

  public RolesController(RoleService roleService)
  {
    _roleService = roleService;
  }

  [HttpPost]
  [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status201Created)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<IActionResult> Create(
      [FromBody] CreateRoleRequest request,
      CancellationToken cancellationToken)
  {
    try
    {
      var role = await _roleService.CreateAsync(request, cancellationToken);
      return CreatedAtAction(nameof(GetById), new { id = role.Id }, role);
    }
    catch (InvalidOperationException ex)
    {
      return Conflict(new { error = ex.Message });
    }
  }

  [HttpGet]
  [ProducesResponseType(typeof(IReadOnlyList<RoleResponse>), StatusCodes.Status200OK)]
  public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
  {
    var roles = await _roleService.GetAllAsync(cancellationToken);
    return Ok(roles);
  }

  [HttpGet("{id:guid}")]
  [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> GetById(
      Guid id,
      CancellationToken cancellationToken)
  {
    var role = await _roleService.GetByIdAsync(id, cancellationToken);
    return role is null ? NotFound() : Ok(role);
  }

  [HttpPost("{id:guid}/parent")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> AssignParent(
      Guid id,
      [FromBody] AssignParentRequest request,
      CancellationToken cancellationToken)
  {
    try
    {
      await _roleService.AssignParentAsync(id, request.ParentRoleId, cancellationToken);
      return NoContent();
    }
    catch (InvalidOperationException ex)
    {
      return NotFound(new { error = ex.Message });
    }
  }
}