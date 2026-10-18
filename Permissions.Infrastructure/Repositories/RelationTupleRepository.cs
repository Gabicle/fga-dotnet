using System.Data;
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

  public async Task<TupleGrantOutcome> GrantAsync(RelationTuple tuple, CancellationToken cancellationToken = default)
  {
    await using var transaction = await _context.Database.BeginTransactionAsync(
        IsolationLevel.ReadCommitted, cancellationToken);

    // A new insertion can wait on an uncommitted unique-index entry which then rolls back.
    // The INSERT's SELECT and its created_at value are evaluated before that wait. The
    // post-statement check below is therefore essential even when the INSERT succeeds.
    for (var attemptNumber = 0; attemptNumber < 2; attemptNumber++)
    {
      var returnedIds = await _context.Database.SqlQuery<Guid>($"""
        WITH attempt AS (
          INSERT INTO relation_tuples AS existing
            (id, object_type, object_id, relation, subject_type, subject_id,
             subject_relation, created_at, expires_at)
          SELECT {tuple.Id}, {tuple.ObjectType}, {tuple.ObjectId}, {tuple.Relation},
                 {tuple.SubjectType}, {tuple.SubjectId}, {tuple.SubjectRelation},
                 clock_timestamp(), {tuple.ExpiresAt}
          WHERE CAST({tuple.ExpiresAt} AS timestamptz) IS NULL
             OR CAST({tuple.ExpiresAt} AS timestamptz) > clock_timestamp()
          ON CONFLICT (object_type, object_id, relation, subject_type, subject_id, subject_relation)
          DO UPDATE SET
            expires_at = EXCLUDED.expires_at,
            created_at = CASE
              WHEN existing.expires_at IS NOT NULL AND existing.expires_at <= clock_timestamp()
                THEN clock_timestamp()
              ELSE existing.created_at
            END
          WHERE (EXCLUDED.expires_at IS NULL OR EXCLUDED.expires_at > clock_timestamp())
            AND ((existing.expires_at IS NOT NULL AND existing.expires_at <= clock_timestamp())
                 OR existing.expires_at IS NOT DISTINCT FROM EXCLUDED.expires_at)
          RETURNING id
        )
        SELECT id AS "Value" FROM attempt
        """).ToListAsync(cancellationToken);

      // This statement runs after any unique-index wait and uses the database's actual
      // clock, rather than a transaction-start or pre-wait timestamp.
      var decisionTime = await _context.Database.SqlQuery<DateTime>(
          $"SELECT clock_timestamp() AS \"Value\"").SingleAsync(cancellationToken);
      if (tuple.ExpiresAt is { } expiresAt && expiresAt <= decisionTime)
      {
        await transaction.RollbackAsync(CancellationToken.None);
        return TupleGrantOutcome.ExpirationElapsed;
      }

      if (returnedIds.Count == 1)
      {
        if (returnedIds[0] == tuple.Id)
        {
          // Only a fresh insertion carries the proposed ID. Refresh its effective
          // creation time after the wait, using one clock value for both the expiry
          // decision and metadata. An elapsed expiry rolls back the original INSERT.
          var refreshed = await _context.Database.ExecuteSqlInterpolatedAsync($"""
              WITH decision AS MATERIALIZED (SELECT clock_timestamp() AS evaluated_at)
              UPDATE relation_tuples AS tuple
              SET created_at = decision.evaluated_at
              FROM decision
              WHERE tuple.id = {tuple.Id}
                AND (CAST({tuple.ExpiresAt} AS timestamptz) IS NULL
                     OR CAST({tuple.ExpiresAt} AS timestamptz) > decision.evaluated_at)
              """, cancellationToken);
          if (refreshed != 1)
          {
            await transaction.RollbackAsync(CancellationToken.None);
            return TupleGrantOutcome.ExpirationElapsed;
          }
        }

        // An existing row was either reactivated after the conflict wait or was
        // idempotently matched; the upsert preserves the latter's metadata.
        await transaction.CommitAsync(cancellationToken);
        return TupleGrantOutcome.Success;
      }

      if (returnedIds.Count != 0)
        throw new InvalidOperationException("Tuple grant returned more than one row.");

      // ON CONFLICT ... WHERE can return no row. Read the authoritative row in a
      // fresh READ COMMITTED statement rather than guessing whether it matched.
      var stored = await _context.RelationTuples.AsNoTracking().SingleOrDefaultAsync(t =>
          t.ObjectType == tuple.ObjectType && t.ObjectId == tuple.ObjectId &&
          t.Relation == tuple.Relation && t.SubjectType == tuple.SubjectType &&
          t.SubjectId == tuple.SubjectId && t.SubjectRelation == tuple.SubjectRelation,
          cancellationToken);
      var observedAt = await _context.Database.SqlQuery<DateTime>(
          $"SELECT clock_timestamp() AS \"Value\"").SingleAsync(cancellationToken);
      if (tuple.ExpiresAt is { } requestedExpiry && requestedExpiry <= observedAt)
      {
        await transaction.RollbackAsync(CancellationToken.None);
        return TupleGrantOutcome.ExpirationElapsed;
      }

      if (stored is not null && (stored.ExpiresAt is null || stored.ExpiresAt > observedAt))
      {
        var result = stored.ExpiresAt == tuple.ExpiresAt
            ? TupleGrantOutcome.Success
            : TupleGrantOutcome.ActiveExpirationConflict;
        if (result == TupleGrantOutcome.Success)
          await transaction.CommitAsync(cancellationToken);
        else
          await transaction.RollbackAsync(CancellationToken.None);
        return result;
      }

      // The row expired between the upsert and classification. Retry while this
      // transaction still holds its conflict lock; a second unexpected miss fails.
    }

    throw new InvalidOperationException("Tuple grant could not classify the stored relationship.");
  }

  public async Task DeleteAsync(TupleKey key, CancellationToken cancellationToken = default)
  {
    await _context.RelationTuples.Where(t =>
        t.ObjectType == key.ObjectType &&
        t.ObjectId == key.ObjectId &&
        t.Relation == key.Relation &&
        t.SubjectType == key.SubjectType &&
        t.SubjectId == key.SubjectId &&
        t.SubjectRelation == key.SubjectRelation)
        .ExecuteDeleteAsync(cancellationToken);
  }
}
