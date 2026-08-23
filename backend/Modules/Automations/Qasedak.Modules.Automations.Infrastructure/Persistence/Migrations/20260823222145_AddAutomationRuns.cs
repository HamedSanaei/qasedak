using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Automations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_runs",
                schema: "automations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AutomationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutomationVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    TriggerEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "automation_run_actions",
                schema: "automations",
                columns: table => new
                {
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionIndex = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AutomationRunRowId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_run_actions", x => new { x.RunId, x.ActionIndex });
                    table.ForeignKey(
                        name: "FK_automation_run_actions_automation_runs_AutomationRunRowId",
                        column: x => x.AutomationRunRowId,
                        principalSchema: "automations",
                        principalTable: "automation_runs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_automation_run_actions_AutomationRunRowId",
                schema: "automations",
                table: "automation_run_actions",
                column: "AutomationRunRowId");

            migrationBuilder.CreateIndex(
                name: "IX_automation_runs_AutomationId_TriggerEventId",
                schema: "automations",
                table: "automation_runs",
                columns: new[] { "AutomationId", "TriggerEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_run_actions",
                schema: "automations");

            migrationBuilder.DropTable(
                name: "automation_runs",
                schema: "automations");
        }
    }
}
