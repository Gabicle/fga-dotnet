using Microsoft.Extensions.DependencyInjection;
using Permissions.Application.Services;

namespace Permissions.Application;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddApplication(this IServiceCollection services)
  {
    services.AddScoped<PermissionCheckEngine>();
    services.AddScoped<PermissionService>();

    return services;
  }
}