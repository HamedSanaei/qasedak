using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Conversations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelAccountId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversations_WorkspaceId_Channel_ParticipantId",
                schema: "conversations",
                table: "conversations");

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelAccountId",
                schema: "conversations",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_exact_thread",
                schema: "conversations",
                table: "conversations",
                columns: new[] { "WorkspaceId", "Channel", "ChannelAccountId", "ParticipantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversations_exact_thread",
                schema: "conversations",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "ChannelAccountId",
                schema: "conversations",
                table: "conversations");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_WorkspaceId_Channel_ParticipantId",
                schema: "conversations",
                table: "conversations",
                columns: new[] { "WorkspaceId", "Channel", "ParticipantId" },
                unique: true);
        }
    }
}
