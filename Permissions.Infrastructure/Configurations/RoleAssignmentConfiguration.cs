using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Permissions.Domain.Entities;

namespace Permissions.Infrastructure.Configurations;

public sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
  public void Configure(EntityTypeBuilder<RoleAssignment> builder)
  {
    builder.ToTable("role_assignments");

    builder.HasKey(x => x.Id);

    builder.Property(x => x.Id)
        .HasColumnName("id");

    builder.Property(x => x.RoleId)
        .HasColumnName("role_id")
        .IsRequired();

    builder.Property(x => x.SubjectType)
        .HasColumnName("subject_type")
        .HasMaxLength(100)
        .IsRequired();

    builder.Property(x => x.SubjectId)
        .HasColumnName("subject_id")
        .HasMaxLength(255)
        .IsRequired();

    builder.Property(x => x.ResourceType)
        .HasColumnName("resource_type")
        .HasMaxLength(100);

    builder.Property(x => x.ResourceId)
        .HasColumnName("resource_id")
        .HasMaxLength(255);

    builder.Property(x => x.CreatedAt)
        .HasColumnName("created_at")
        .IsRequired();

    builder.Property(x => x.ExpiresAt)
        .HasColumnName("expires_at");

    builder.HasOne(x => x.Role)
        .WithMany()
        .HasForeignKey(x => x.RoleId)
        .OnDelete(DeleteBehavior.Cascade);

    builder.HasIndex(x => new { x.SubjectType, x.SubjectId, x.RoleId, x.ResourceType, x.ResourceId })
        .IsUnique()
        .HasDatabaseName("ix_role_assignments_lookup");

    builder.HasIndex(x => new { x.SubjectType, x.SubjectId })
        .HasDatabaseName("ix_role_assignments_subject");
  }
}