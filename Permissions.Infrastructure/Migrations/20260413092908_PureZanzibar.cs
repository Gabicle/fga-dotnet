using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Permissions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PureZanzibar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_assignments");

            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at",
                table: "relation_tuples",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_relation_tuples_expires_at",
                table: "relation_tuples",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_relation_tuples_expires_at",
                table: "relation_tuples");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "relation_tuples");

            migrationBuilder.CreateTable(
                name: "role_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resource_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    subject_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    subject_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_role_assignments_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_assignments_lookup",
                table: "role_assignments",
                columns: new[] { "subject_type", "subject_id", "role_id", "resource_type", "resource_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_role_id",
                table: "role_assignments",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_assignments_subject",
                table: "role_assignments",
                columns: new[] { "subject_type", "subject_id" });
        }
    }
}
