namespace Permissions.Application.DTOs;

public static class AuthorizationReasonCodes
{
  public const string Allowed = "authorization_allowed";
  public const string RelationshipDenied = "relationship_denied";
  public const string RequiredAbacDenied = "required_abac_denied";
  public const string InvalidContext = "invalid_authorization_context";
  public const string EvaluationError = "authorization_evaluation_error";
}
