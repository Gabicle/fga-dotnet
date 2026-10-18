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
  private readonly string? _connectionString;
  private readonly string? _destructiveAuthorization;

  public DatabaseFixture() : this(
      Environment.GetEnvironmentVariable(DisposableTestDatabase.ConnectionStringVariable),
      Environment.GetEnvironmentVariable(DisposableTestDatabase.AuthorizationVariable)) { }

  internal DatabaseFixture(string? connectionString, string? destructiveAuthorization)
  {
    _connectionString = connectionString;
    _destructiveAuthorization = destructiveAuthorization;
  }

  private string SafeConnectionString => DisposableTestDatabase.Validate(
      _connectionString, _destructiveAuthorization).ConnectionString;

  public IServiceProvider Services { get; private set; } = null!;

  public async Task InitializeAsync()
  {
    var connectionString = SafeConnectionString;
    var services = new ServiceCollection();

    services.AddDbContext<PermissionsDbContext>(options =>
        options.UseNpgsql(connectionString));

    services.AddScoped<IRelationTupleRepository, RelationTupleRepository>();
    services.AddScoped<IRoleRepository, RoleRepository>();
    services.AddScoped<PermissionCheckEngine>();
    services.AddScoped<PermissionService>();
    services.AddScoped<RoleService>();
    services.AddScoped<AbacEvaluator>();

    services.AddSingleton<IAbacPolicy>(new RegionMatchPolicy("report", "viewer"));
    services.AddSingleton<IAbacPolicy>(new RegionMatchPolicy("report", "editor"));
    services.AddSingleton<IAbacPolicy>(new ResourceStatusPolicy("report", "editor", "locked", "archived"));
    services.AddSingleton<IAbacPolicy>(new SensitivityLevelPolicy("report", "viewer"));
    services.AddSingleton<IAbacPolicy>(new SensitivityLevelPolicy("report", "editor"));

    Services = services.BuildServiceProvider();

    using (var validationScope = Services.CreateScope())
      validationScope.ServiceProvider.GetRequiredService<AbacEvaluator>()
          .ValidateRequiredPolicies();

    // Run migrations against the test database
    using var scope = Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<PermissionsDbContext>();
    await context.Database.MigrateAsync();
  }

  public async Task DisposeAsync()
  {
    // Recheck before cleanup even if configuration was invalid at startup.
    _ = SafeConnectionString;
    if (Services is null) return;
    // Drop the dedicated test database between full test runs.
    using var scope = Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<PermissionsDbContext>();
    await context.Database.EnsureDeletedAsync();
  }
}
