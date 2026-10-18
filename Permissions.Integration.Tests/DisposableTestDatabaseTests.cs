namespace Permissions.Integration.Tests;

public sealed class DisposableTestDatabaseTests
{
  private const string SafeTarget =
      "Host=127.0.0.1;Port=1;Database=permissions_integration_disposable;Username=postgres;Password=synthetic-secret";

  [Theory]
  [InlineData(null, DisposableTestDatabase.AuthorizationValue)]
  [InlineData("", DisposableTestDatabase.AuthorizationValue)]
  [InlineData("not-a-connection-string", DisposableTestDatabase.AuthorizationValue)]
  [InlineData("Host=127.0.0.1;Port=not-a-port;Database=permissions_integration_disposable;Username=postgres;Password=synthetic-secret", DisposableTestDatabase.AuthorizationValue)]
  [InlineData("Host=127.0.0.1;Port=1;Username=postgres;Password=synthetic-secret", DisposableTestDatabase.AuthorizationValue)]
  [InlineData("Host=127.0.0.1;Port=1;Database=permissions_integration_disposable;Password=synthetic-secret", DisposableTestDatabase.AuthorizationValue)]
  [InlineData("Host=127.0.0.1;Port=1;Database=postgres;Username=postgres;Password=synthetic-secret", DisposableTestDatabase.AuthorizationValue)]
  [InlineData("Host=127.0.0.1;Port=1;Database=template0;Username=postgres;Password=synthetic-secret", DisposableTestDatabase.AuthorizationValue)]
  [InlineData("Host=127.0.0.1;Port=1;Database=template1;Username=postgres;Password=synthetic-secret", DisposableTestDatabase.AuthorizationValue)]
  [InlineData("Host=127.0.0.1;Port=1;Database=permissions;Username=postgres;Password=synthetic-secret", DisposableTestDatabase.AuthorizationValue)]
  [InlineData("Host=127.0.0.1;Port=1;Database=permissions_integration_test;Username=postgres;Password=synthetic-secret", DisposableTestDatabase.AuthorizationValue)]
  [InlineData("Host=127.0.0.1;Port=1;Database=Permissions_Integration_Disposable;Username=postgres;Password=synthetic-secret", DisposableTestDatabase.AuthorizationValue)]
  [InlineData(SafeTarget, null)]
  [InlineData(SafeTarget, "true")]
  public async Task InvalidTargetOrAuthorizationFailsBeforeDatabaseAccess(
      string? connectionString, string? authorization)
  {
    var fixture = new DatabaseFixture(connectionString, authorization);

    var startup = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InitializeAsync());
    var cleanup = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.DisposeAsync());

    Assert.DoesNotContain("synthetic-secret", startup.Message);
    Assert.DoesNotContain("synthetic-secret", cleanup.Message);
    Assert.Null(fixture.Services);
  }

  [Fact]
  public void RecognizableDisposableTargetWithExplicitAuthorizationPassesValidation()
  {
    var target = DisposableTestDatabase.Validate(SafeTarget,
        DisposableTestDatabase.AuthorizationValue);

    Assert.Equal("permissions_integration_disposable", target.Database);
    Assert.Equal("127.0.0.1", target.Host);
  }
}
