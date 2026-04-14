using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Permissions.Domain.Repositories;
using Permissions.Infrastructure.Repositories;
using QueryProfiler.Core.Diagnostics;

namespace Permissions.Infrastructure;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddInfrastructure(
      this IServiceCollection services,
      IConfiguration configuration)
  {
    services.AddDbContext<PermissionsDbContext>((sp, options) =>
 {
   options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
   options.AddInterceptors(sp.GetRequiredService<QueryProfilingInterceptor>());
 });

    services.AddScoped<IRelationTupleRepository, RelationTupleRepository>();
    services.AddScoped<IRoleRepository, RoleRepository>();

    return services;
  }
}