using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Permissions.Application.Services;
using Permissions.Infrastructure;

namespace Permissions.Integration.Tests;

public abstract class IntegrationTestBase : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
  protected readonly IServiceScope Scope;
  protected readonly PermissionService PermissionService;
  protected readonly PermissionsDbContext DbContext;

  protected IntegrationTestBase(DatabaseFixture fixture)
  {
    Scope = fixture.Services.CreateScope();
    PermissionService = Scope.ServiceProvider.GetRequiredService<PermissionService>();
    DbContext = Scope.ServiceProvider.GetRequiredService<PermissionsDbContext>();
  }

  public Task InitializeAsync() => Task.CompletedTask;

  public async Task DisposeAsync()
  {
    // Clean all tuples and roles between tests
    await DbContext.RelationTuples.ExecuteDeleteAsync();
    await DbContext.Roles.ExecuteDeleteAsync();
    Scope.Dispose();
  }
}