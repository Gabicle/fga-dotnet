using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Permissions.Application.Policies;
using Permissions.Application.Policies.BuiltIn;
using Permissions.Application.Services;
using Permissions.Domain.Repositories;
using Permissions.Infrastructure;
using Permissions.Infrastructure.Repositories;

namespace Permissions.Integration.Tests;

public sealed class DatabaseFixture : IAsyncLifetime
{
  private static string ConnectionString =>
    Environment.GetEnvironmentVariable("INTEGRATION_TEST_CONNECTION_STRING")
    ?? "Host=localhost;Port=5437;Database=permissions_integration_test;Username=postgres;Password=postgres";

  public IServiceProvider Services { get; private set; } = null!;

  public async Task InitializeAsync()
  {
    var services = new ServiceCollection();

    services.AddDbContext<PermissionsDbContext>(options =>
        options.UseNpgsql(ConnectionString));

    services.AddScoped<IRelationTupleRepository, RelationTupleRepository>();
    services.AddScoped<IRoleRepository, RoleRepository>();
    services.AddScoped<PermissionCheckEngine>();
    services.AddScoped<PermissionService>();
    services.AddScoped<AbacEvaluator>();

    services.AddSingleton<IAbacPolicy>(new RegionMatchPolicy("report", "viewer"));
    services.AddSingleton<IAbacPolicy>(new RegionMatchPolicy("report", "editor"));
    services.AddSingleton<IAbacPolicy>(new ResourceStatusPolicy("report", "editor", "locked", "archived"));
    services.AddSingleton<IAbacPolicy>(new SensitivityLevelPolicy("report", "viewer"));
    services.AddSingleton<IAbacPolicy>(new SensitivityLevelPolicy("report", "editor"));

    Services = services.BuildServiceProvider();

    // Run migrations against the test database
    using var scope = Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<PermissionsDbContext>();
    await context.Database.MigrateAsync();
  }

  public async Task DisposeAsync()
  {
    // Drop and recreate the test database between full test runs
    using var scope = Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<PermissionsDbContext>();
    await context.Database.EnsureDeletedAsync();
  }
}