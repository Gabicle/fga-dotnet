using Permissions.Domain.Entities;

namespace Permissions.Domain.Repositories;

public interface IRoleAssignmentRepository
{
  Task<IReadOnlyList<RoleAssignment>> GetBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<RoleAssignment>> GetBySubjectAndResourceAsync(string subjectType, string subjectId, string resourceType, string resourceId, CancellationToken cancellationToken = default);
  Task<bool> ExistsAsync(Guid roleId, string subjectType, string subjectId, string? resourceType, string? resourceId, CancellationToken cancellationToken = default);
  Task AddAsync(RoleAssignment assignment, CancellationToken cancellationToken = default);
  Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}