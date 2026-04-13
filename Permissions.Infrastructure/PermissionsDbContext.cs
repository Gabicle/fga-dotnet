using Microsoft.EntityFrameworkCore;
using Permissions.Domain.Entities;

namespace Permissions.Infrastructure;

public sealed class PermissionsDbContext : DbContext
{
  public DbSet<RelationTuple> RelationTuples => Set<RelationTuple>();
  public DbSet<Role> Roles => Set<Role>();

  public PermissionsDbContext(DbContextOptions<PermissionsDbContext> options)
      : base(options) { }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(PermissionsDbContext).Assembly);
  }
}