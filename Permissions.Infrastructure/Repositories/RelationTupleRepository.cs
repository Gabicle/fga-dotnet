using Microsoft.EntityFrameworkCore;
using Permissions.Domain.Entities;
using Permissions.Domain.Repositories;
using Permissions.Domain.ValueObjects;

namespace Permissions.Infrastructure.Repositories;

public sealed class RelationTupleRepository : IRelationTupleRepository
{
  private readonly PermissionsDbContext _context;

  public RelationTupleRepository(PermissionsDbContext context)
  {
    _context = context;
  }

  public async Task<bool> ExistsAsync(TupleKey key, CancellationToken cancellationToken = default)
  {
    return await _context.RelationTuples.AnyAsync(t =>
        t.ObjectType == key.ObjectType &&
        t.ObjectId == key.ObjectId &&
        t.Relation == key.Relation &&
        t.SubjectType == key.SubjectType &&
        t.SubjectId == key.SubjectId &&
        t.SubjectRelation == key.SubjectRelation &&
        (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow),
        cancellationToken);
  }

  public async Task<IReadOnlyList<RelationTuple>> GetByObjectAsync(
      string objectType,
      string objectId,
      CancellationToken cancellationToken = default)
  {
    return await _context.RelationTuples
        .Where(t => t.ObjectType == objectType &&
                    t.ObjectId == objectId &&
                    (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
        .AsNoTracking()
        .ToListAsync(cancellationToken);
  }

  public async Task<IReadOnlyList<RelationTuple>> GetBySubjectAsync(
      string subjectType,
      string subjectId,
      CancellationToken cancellationToken = default)
  {
    return await _context.RelationTuples
        .Where(t => t.SubjectType == subjectType &&
                    t.SubjectId == subjectId &&
                    (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
        .AsNoTracking()
        .ToListAsync(cancellationToken);
  }

  public async Task<IReadOnlyList<RelationTuple>> GetByObjectAndRelationAsync(
      string objectType,
      string objectId,
      string relation,
      CancellationToken cancellationToken = default)
  {
    return await _context.RelationTuples
        .Where(t =>
            t.ObjectType == objectType &&
            t.ObjectId == objectId &&
            t.Relation == relation &&
            (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
        .AsNoTracking()
        .ToListAsync(cancellationToken);
  }

  public async Task<IReadOnlyList<RelationTuple>> GetBySubjectWithRelationAsync(
      string subjectType,
      string subjectId,
      string subjectRelation,
      CancellationToken cancellationToken = default)
  {
    return await _context.RelationTuples
        .Where(t =>
            t.SubjectType == subjectType &&
            t.SubjectId == subjectId &&
            t.SubjectRelation == subjectRelation &&
            (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow))
        .AsNoTracking()
        .ToListAsync(cancellationToken);
  }

  public async Task AddAsync(RelationTuple tuple, CancellationToken cancellationToken = default)
  {
    await _context.RelationTuples.AddAsync(tuple, cancellationToken);
    await _context.SaveChangesAsync(cancellationToken);
  }

  public async Task DeleteAsync(TupleKey key, CancellationToken cancellationToken = default)
  {
    var tuple = await _context.RelationTuples.FirstOrDefaultAsync(t =>
        t.ObjectType == key.ObjectType &&
        t.ObjectId == key.ObjectId &&
        t.Relation == key.Relation &&
        t.SubjectType == key.SubjectType &&
        t.SubjectId == key.SubjectId,
        cancellationToken);

    if (tuple is not null)
    {
      _context.RelationTuples.Remove(tuple);
      await _context.SaveChangesAsync(cancellationToken);
    }
  }
}