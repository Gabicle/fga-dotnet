using Permissions.Domain.Entities;
using Permissions.Domain.ValueObjects;

namespace Permissions.Domain.Repositories;

public interface IRelationTupleRepository
{
  Task<bool> ExistsAsync(TupleKey key, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<RelationTuple>> GetByObjectAsync(string objectType, string objectId, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<RelationTuple>> GetBySubjectAsync(string subjectType, string subjectId, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<RelationTuple>> GetByObjectAndRelationAsync(string objectType, string objectId, string relation, CancellationToken cancellationToken = default);
  Task AddAsync(RelationTuple tuple, CancellationToken cancellationToken = default);
  Task DeleteAsync(TupleKey key, CancellationToken cancellationToken = default);
}