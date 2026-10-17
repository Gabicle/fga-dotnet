using System.Data;
using Microsoft.EntityFrameworkCore;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;

namespace Permissions.Infrastructure.Repositories;

public sealed class RoleRepository : IRoleRepository
{
  // All service-managed parent writes share this PostgreSQL advisory lock.
  public const long HierarchyMutationLockKey = 0x4647415F524F4C45;
  private readonly PermissionsDbContext _context;

  public RoleRepository(PermissionsDbContext context)
  {
    _context = context;
  }

  public async Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
  {
    return await _context.Roles
        .Include(r => r.ParentRole)
        .AsNoTracking()
        .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
  }

  public async Task<Role?> GetByNameAsync(string normalizedName, CancellationToken cancellationToken = default)
  {
    return await _context.Roles
        .Include(r => r.ParentRole)
        .AsNoTracking()
        .FirstOrDefaultAsync(r => r.NormalizedName == normalizedName, cancellationToken);
  }

  public async Task<IReadOnlyList<Role>> GetChildrenByParentIdAsync(
      Guid parentRoleId,
      CancellationToken cancellationToken = default)
  {
    return await _context.Roles
        .Where(r => r.ParentRoleId == parentRoleId)
        .AsNoTracking()
        .ToListAsync(cancellationToken);
  }

  public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default)
  {
    return await _context.Roles
        .Include(r => r.ParentRole)
        .AsNoTracking()
        .ToListAsync(cancellationToken);
  }

  public async Task<IReadOnlyList<Role>> GetInheritanceChainAsync(
      Guid roleId,
      CancellationToken cancellationToken = default)
  {
    var chain = new List<Role>();
    var visited = new HashSet<Guid>();
    var currentId = (Guid?)roleId;

    while (currentId.HasValue)
    {
      if (!visited.Add(currentId.Value))
        throw new InvalidOperationException("Role hierarchy contains a cycle.");
      if (visited.Count > 1024)
        throw new InvalidOperationException("Role hierarchy traversal limit exceeded.");

      var role = await _context.Roles
          .AsNoTracking()
          .FirstOrDefaultAsync(r => r.Id == currentId.Value, cancellationToken);

      if (role is null)
      {
        if (chain.Count == 0) return chain;
        throw new InvalidOperationException("Role hierarchy contains a missing parent.");
      }

      chain.Add(role);
      currentId = role.ParentRoleId;
    }

    return chain;
  }

  public async Task<T> ExecuteHierarchyMutationAsync<T>(
      Func<CancellationToken, Task<T>> operation,
      CancellationToken cancellationToken = default)
  {
    // READ COMMITTED gives each validation query a fresh snapshot after any
    // prior lock holder commits. REPEATABLE READ could retain stale hierarchy data.
    await using var transaction = await _context.Database.BeginTransactionAsync(
        IsolationLevel.ReadCommitted, cancellationToken);
    await _context.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock({HierarchyMutationLockKey})", cancellationToken);

    var result = await operation(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    // Disposal rolls back an uncommitted transaction on error or cancellation,
    // and PostgreSQL releases this transaction-scoped lock in either case.
    return result;
  }

  public async Task AddAsync(Role role, CancellationToken cancellationToken = default)
  {
    await _context.Roles.AddAsync(role, cancellationToken);
    await _context.SaveChangesAsync(cancellationToken);
  }

  public async Task UpdateAsync(Role role, CancellationToken cancellationToken = default)
  {
    // Parent assignment is the only current role update. Updating the scalar
    // avoids attaching a detached parent graph to a context that may already
    // track roles created earlier in the same request scope.
    var updated = await _context.Roles
        .Where(r => r.Id == role.Id)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(r => r.ParentRoleId, role.ParentRoleId), cancellationToken);
    if (updated != 1)
      throw new InvalidOperationException("Role to update was not found.");
  }

  public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
  {
    var role = await _context.Roles
        .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    if (role is not null)
    {
      _context.Roles.Remove(role);
      await _context.SaveChangesAsync(cancellationToken);
    }
  }
}
