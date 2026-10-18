namespace Permissions.Domain.Repositories;

public enum TupleGrantOutcome
{
  Success,
  ActiveExpirationConflict,
  ExpirationElapsed
}
