using Microsoft.Extensions.DependencyInjection;
using Permissions.Application.Policies;
using Permissions.Application.Policies.BuiltIn;
using Permissions.Application.Services;

namespace Permissions.Application;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddApplication(this IServiceCollection services)
  {
    services.AddScoped<PermissionCheckEngine>();
    services.AddScoped<PermissionService>();
    services.AddScoped<AbacEvaluator>();

    // Built-in ABAC policies
    // Region match — users can only access resources in their own region
    services.AddSingleton<IAbacPolicy>(
        new RegionMatchPolicy("report", "viewer"));
    services.AddSingleton<IAbacPolicy>(
        new RegionMatchPolicy("report", "editor"));

    // Resource status — nobody can edit locked or archived reports
    services.AddSingleton<IAbacPolicy>(
        new ResourceStatusPolicy("report", "editor", "locked", "archived"));

    // Sensitivity level — user clearance must meet resource sensitivity
    services.AddSingleton<IAbacPolicy>(
        new SensitivityLevelPolicy("report", "viewer"));
    services.AddSingleton<IAbacPolicy>(
        new SensitivityLevelPolicy("report", "editor"));

    return services;
  }
}