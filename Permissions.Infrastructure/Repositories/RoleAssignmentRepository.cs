using Microsoft.EntityFrameworkCore;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;

namespace Permissions.Infrastructure.Repositories;

public sealed class RoleAssignmentRepository : IRoleAssignmentRepository
{
  private readonly PermissionsDbContext _context;

  public RoleAssignmentRepository(PermissionsDbContext context)
  {
    _context = context;
  }

  public async Task<IReadOnlyList<RoleAssignment>> GetBySubjectAsync(
      string subjectType,
      string subjectId,
      CancellationToken cancellationToken = default)
  {
    return await _context.RoleAssignments
        .Include(a => a.Role)
        .Where(a => a.SubjectType == subjectType && a.SubjectId == subjectId)
        .AsNoTracking()
        .ToListAsync(cancellationToken);
  }

  public async Task<IReadOnlyList<RoleAssignment>> GetBySubjectAndResourceAsync(
      string subjectType,
      string subjectId,
      string resourceType,
      string resourceId,
      CancellationToken cancellationToken = default)
  {
    return await _context.RoleAssignments
        .Include(a => a.Role)
        .Where(a =>
            a.SubjectType == subjectType &&
            a.SubjectId == subjectId &&
            a.ResourceType == resourceType &&
            a.ResourceId == resourceId)
        .AsNoTracking()
        .ToListAsync(cancellationToken);
  }

  public async Task<bool> ExistsAsync(
      Guid roleId,
      string subjectType,
      string subjectId,
      string? resourceType,
      string? resourceId,
      CancellationToken cancellationToken = default)
  {
    return await _context.RoleAssignments.AnyAsync(a =>
        a.RoleId == roleId &&
        a.SubjectType == subjectType &&
        a.SubjectId == subjectId &&
        a.ResourceType == resourceType &&
        a.ResourceId == resourceId,
        cancellationToken);
  }

  public async Task AddAsync(RoleAssignment assignment, CancellationToken cancellationToken = default)
  {
    await _context.RoleAssignments.AddAsync(assignment, cancellationToken);
    await _context.SaveChangesAsync(cancellationToken);
  }

  public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
  {
    var assignment = await _context.RoleAssignments
        .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    if (assignment is not null)
    {
      _context.RoleAssignments.Remove(assignment);
      await _context.SaveChangesAsync(cancellationToken);
    }
  }
}