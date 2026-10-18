namespace Permissions.Application.Services;

public sealed class InvalidAuthorizationContextException : ArgumentException
{
  public InvalidAuthorizationContextException()
      : base("Invalid authorization context.") { }
}
