using Microsoft.EntityFrameworkCore;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;

namespace Permissions.Infrastructure.Repositories;

public sealed class RoleRepository : IRoleRepository
{
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
    var currentId = (Guid?)roleId;

    while (currentId.HasValue)
    {
      var role = await _context.Roles
          .AsNoTracking()
          .FirstOrDefaultAsync(r => r.Id == currentId.Value, cancellationToken);

      if (role is null) break;

      chain.Add(role);
      currentId = role.ParentRoleId;
    }

    return chain;
  }

  public async Task AddAsync(Role role, CancellationToken cancellationToken = default)
  {
    await _context.Roles.AddAsync(role, cancellationToken);
    await _context.SaveChangesAsync(cancellationToken);
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