using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Automations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelAccountBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChannelAccountId",
                schema: "automations",
                table: "automations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_automations_WorkspaceId_ChannelAccountId",
                schema: "automations",
                table: "automations",
                columns: new[] { "WorkspaceId", "ChannelAccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_automations_WorkspaceId_ChannelAccountId",
                schema: "automations",
                table: "automations");

            migrationBuilder.DropColumn(
                name: "ChannelAccountId",
                schema: "automations",
                table: "automations");
        }
    }
}
