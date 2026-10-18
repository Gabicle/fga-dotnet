namespace Permissions.Application.Services;

public sealed class TupleGrantConflictException : Exception
{
  public TupleGrantConflictException()
      : base("An active tuple already has a different expiration.") { }
}
