using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Permissions.Domain.Entities;

namespace Permissions.Infrastructure.Configurations;

public sealed class RelationTupleConfiguration : IEntityTypeConfiguration<RelationTuple>
{
  public void Configure(EntityTypeBuilder<RelationTuple> builder)
  {
    builder.ToTable("relation_tuples");

    builder.HasKey(x => x.Id);

    builder.Property(x => x.Id)
        .HasColumnName("id");

    builder.Property(x => x.ObjectType)
        .HasColumnName("object_type")
        .HasMaxLength(100)
        .IsRequired();

    builder.Property(x => x.ObjectId)
        .HasColumnName("object_id")
        .HasMaxLength(255)
        .IsRequired();

    builder.Property(x => x.Relation)
        .HasColumnName("relation")
        .HasMaxLength(100)
        .IsRequired();

    builder.Property(x => x.SubjectType)
        .HasColumnName("subject_type")
        .HasMaxLength(100)
        .IsRequired();

    builder.Property(x => x.SubjectId)
        .HasColumnName("subject_id")
        .HasMaxLength(255)
        .IsRequired();

    builder.Property(x => x.SubjectRelation)
        .HasColumnName("subject_relation")
        .HasMaxLength(100);

    builder.Property(x => x.CreatedAt)
        .HasColumnName("created_at")
        .IsRequired();

    builder.HasIndex(x => new { x.ObjectType, x.ObjectId, x.Relation, x.SubjectType, x.SubjectId })
        .IsUnique()
        .HasDatabaseName("ix_relation_tuples_lookup");

    builder.HasIndex(x => new { x.SubjectType, x.SubjectId })
        .HasDatabaseName("ix_relation_tuples_subject");
  }
}