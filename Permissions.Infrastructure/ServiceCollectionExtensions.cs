using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Permissions.Domain.Repositories;
using Permissions.Infrastructure.Repositories;

namespace Permissions.Infrastructure;

public static class ServiceCollectionExtensions
{
  public static IServiceCollection AddInfrastructure(
      this IServiceCollection services,
      IConfiguration configuration)
  {
    services.AddDbContext<PermissionsDbContext>(options =>
        options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

    services.AddScoped<IRelationTupleRepository, RelationTupleRepository>();
    services.AddScoped<IRoleRepository, RoleRepository>();

    return services;
  }
}