namespace Permissions.Application.Services;

public sealed class RoleNotFoundException : InvalidOperationException
{
  public RoleNotFoundException() : base("Role not found.") { }
}

public sealed class RoleConflictException : InvalidOperationException
{
  public RoleConflictException() : base("Role operation conflicts with existing role state.") { }
}
