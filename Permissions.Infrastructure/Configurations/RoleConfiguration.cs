using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Permissions.Domain.Entities;

namespace Permissions.Infrastructure.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
  public void Configure(EntityTypeBuilder<Role> builder)
  {
    builder.ToTable("roles");

    builder.HasKey(x => x.Id);

    builder.Property(x => x.Id)
        .HasColumnName("id");

    builder.Property(x => x.Name)
        .HasColumnName("name")
        .HasMaxLength(100)
        .IsRequired();

    builder.Property(x => x.NormalizedName)
        .HasColumnName("normalized_name")
        .HasMaxLength(100)
        .IsRequired();

    builder.Property(x => x.Description)
        .HasColumnName("description")
        .HasMaxLength(500);

    builder.Property(x => x.ParentRoleId)
        .HasColumnName("parent_role_id");

    builder.Property(x => x.CreatedAt)
        .HasColumnName("created_at")
        .IsRequired();

    builder.HasOne(x => x.ParentRole)
        .WithMany(x => x.ChildRoles)
        .HasForeignKey(x => x.ParentRoleId)
        .OnDelete(DeleteBehavior.Restrict);

    builder.HasIndex(x => x.NormalizedName)
        .IsUnique()
        .HasDatabaseName("ix_roles_normalized_name");
  }
}