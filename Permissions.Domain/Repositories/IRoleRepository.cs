using Permissions.Domain.Entities;

namespace Permissions.Domain.Repositories;

public interface IRoleRepository
{
  Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
  Task<Role?> GetByNameAsync(string normalizedName, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default);
  Task<IReadOnlyList<Role>> GetInheritanceChainAsync(Guid roleId, CancellationToken cancellationToken = default);
  Task AddAsync(Role role, CancellationToken cancellationToken = default);
  Task UpdateAsync(Role role, CancellationToken cancellationToken = default);
  Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}