using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Permissions.Api.Security;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
  public const string SchemeName = "ApiKey";
  private readonly ApiKeyCredentials _credentials;

  public ApiKeyAuthenticationHandler(
      IOptionsMonitor<AuthenticationSchemeOptions> options,
      ILoggerFactory logger,
      UrlEncoder encoder,
      ApiKeyCredentials credentials) : base(options, logger, encoder)
  {
    _credentials = credentials;
  }

  protected override Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    if (!Request.Headers.TryGetValue("Authorization", out var headers))
      return Task.FromResult(AuthenticateResult.NoResult());

    if (headers.Count != 1 || headers[0] is not { } header ||
        !header.StartsWith("ApiKey ", StringComparison.Ordinal) ||
        header.Length != "ApiKey ".Length + 44)
      return Task.FromResult(AuthenticateResult.Fail("Malformed API-key authorization header."));

    var supplied = new byte[ApiKeyCredentials.KeySizeBytes];
    if (!Convert.TryFromBase64Chars(header.AsSpan("ApiKey ".Length), supplied, out var written) ||
        written != ApiKeyCredentials.KeySizeBytes)
      return Task.FromResult(AuthenticateResult.Fail("Malformed API-key authorization header."));

    // Evaluate both comparisons so the selected capability does not change comparison work.
    var isCheck = CryptographicOperations.FixedTimeEquals(supplied, _credentials.CheckKey);
    var isAdmin = CryptographicOperations.FixedTimeEquals(supplied, _credentials.AdminKey);
    if (!isCheck && !isAdmin)
      return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

    var capability = isAdmin ? ApiKeyPolicies.Admin : ApiKeyPolicies.Check;
    var clientId = isAdmin ? "admin-client" : "check-client";
    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, clientId),
         new Claim(ApiKeyPolicies.CapabilityClaim, capability)], SchemeName);
    var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
    return Task.FromResult(AuthenticateResult.Success(ticket));
  }
}
