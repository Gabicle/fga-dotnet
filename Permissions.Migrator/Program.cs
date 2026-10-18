using Microsoft.EntityFrameworkCore;
using Permissions.Infrastructure;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
  Console.Error.WriteLine("Database connection configuration is required for migrations.");
  return 1;
}

try
{
  var options = new DbContextOptionsBuilder<PermissionsDbContext>()
      .UseNpgsql(connectionString)
      .Options;
  await using var context = new PermissionsDbContext(options);
  await context.Database.MigrateAsync();
  Console.WriteLine("EF Core migrations applied.");
  return 0;
}
catch (Exception ex)
{
  // Connection and provider errors can contain sensitive configuration details.
  Console.Error.WriteLine($"Database migration failed ({ex.GetType().Name}).");
  return 1;
}
