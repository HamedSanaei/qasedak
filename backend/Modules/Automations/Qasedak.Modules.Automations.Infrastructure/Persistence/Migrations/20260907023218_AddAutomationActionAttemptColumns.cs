using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Automations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationActionAttemptColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AttemptedAtUtc",
                schema: "automations",
                table: "automation_run_actions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAtUtc",
                schema: "automations",
                table: "automation_run_actions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderMessageId",
                schema: "automations",
                table: "automation_run_actions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderRecipientId",
                schema: "automations",
                table: "automation_run_actions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttemptedAtUtc",
                schema: "automations",
                table: "automation_run_actions");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                schema: "automations",
                table: "automation_run_actions");

            migrationBuilder.DropColumn(
                name: "ProviderMessageId",
                schema: "automations",
                table: "automation_run_actions");

            migrationBuilder.DropColumn(
                name: "ProviderRecipientId",
                schema: "automations",
                table: "automation_run_actions");
        }
    }
}
