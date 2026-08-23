using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Automations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAutomationsCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "automations");

            migrationBuilder.CreateTable(
                name: "automations",
                schema: "automations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActivatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisabledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CurrentVersionFrozen = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "automation_versions",
                schema: "automations",
                columns: table => new
                {
                    AutomationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    definition_json = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_versions", x => new { x.AutomationId, x.Number });
                    table.ForeignKey(
                        name: "FK_automation_versions_automations_AutomationId",
                        column: x => x.AutomationId,
                        principalSchema: "automations",
                        principalTable: "automations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_automation_versions_AutomationId",
                schema: "automations",
                table: "automation_versions",
                column: "AutomationId");

            migrationBuilder.CreateIndex(
                name: "IX_automations_WorkspaceId_CreatedAtUtc",
                schema: "automations",
                table: "automations",
                columns: new[] { "WorkspaceId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_versions",
                schema: "automations");

            migrationBuilder.DropTable(
                name: "automations",
                schema: "automations");
        }
    }
}
