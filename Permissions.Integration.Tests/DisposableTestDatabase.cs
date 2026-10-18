using System.Text.RegularExpressions;
using Npgsql;

namespace Permissions.Integration.Tests;

// Test-only guard. Validate the *target* before connecting, migrating, creating, or deleting it.
internal static class DisposableTestDatabase
{
  internal const string ConnectionStringVariable = "INTEGRATION_TEST_CONNECTION_STRING";
  internal const string AuthorizationVariable = "PERMISSIONS_TEST_ALLOW_DESTRUCTIVE_DATABASE";
  internal const string AuthorizationValue = "disposable-tests-only";

  private static readonly Regex NamePattern = new(
      @"^permissions_(integration|tuple_migration)_disposable(_[a-z0-9]+)?$",
      RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

  internal static NpgsqlConnectionStringBuilder Validate(string? connectionString, string? authorization)
  {
    if (authorization != AuthorizationValue)
      throw new InvalidOperationException(
          $"Set {AuthorizationVariable}={AuthorizationValue} to use a disposable test database.");

    if (string.IsNullOrWhiteSpace(connectionString))
      throw new InvalidOperationException($"{ConnectionStringVariable} must be configured.");

    NpgsqlConnectionStringBuilder target;
    try
    {
      target = new NpgsqlConnectionStringBuilder(connectionString);
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
    {
      // A connection string may contain a password. Never include it in this error.
      throw new InvalidOperationException($"{ConnectionStringVariable} is malformed.");
    }

    if (!target.ContainsKey("Host") || string.IsNullOrWhiteSpace(target.Host) ||
        !target.ContainsKey("Database") || string.IsNullOrWhiteSpace(target.Database) ||
        !target.ContainsKey("Username") || string.IsNullOrWhiteSpace(target.Username))
      throw new InvalidOperationException($"{ConnectionStringVariable} needs an explicit host, database, and username.");

    if (target.Database is "postgres" or "template0" or "template1" ||
        !NamePattern.IsMatch(target.Database))
      throw new InvalidOperationException("The integration database name must identify a disposable test database.");

    return target;
  }

  internal static NpgsqlConnectionStringBuilder ValidateConfiguredTarget(string? connectionString) => Validate(
      connectionString, Environment.GetEnvironmentVariable(AuthorizationVariable));
}
