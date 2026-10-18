using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Permissions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TupleIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_relation_tuples_identity",
                table: "relation_tuples",
                columns: new[] { "object_type", "object_id", "relation", "subject_type", "subject_id", "subject_relation" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.DropIndex(
                name: "ix_relation_tuples_lookup",
                table: "relation_tuples");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_relation_tuples_lookup",
                table: "relation_tuples",
                columns: new[] { "object_type", "object_id", "relation", "subject_type", "subject_id" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_relation_tuples_identity",
                table: "relation_tuples");
        }
    }
}
