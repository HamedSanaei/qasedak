using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Conversations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryImportAndScopedProviderMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_messages_ConversationId",
                schema: "conversations",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_messages_ProviderMessageId",
                schema: "conversations",
                table: "messages");

            migrationBuilder.AddColumn<int>(
                name: "ContentKind",
                schema: "conversations",
                table: "messages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ImportSource",
                schema: "conversations",
                table: "messages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "WebhookObserved",
                schema: "conversations",
                table: "messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_messages_conversation_provider_message",
                schema: "conversations",
                table: "messages",
                columns: new[] { "ConversationId", "ProviderMessageId" },
                unique: true,
                filter: "\"ProviderMessageId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_messages_conversation_provider_message",
                schema: "conversations",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "ContentKind",
                schema: "conversations",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "ImportSource",
                schema: "conversations",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "WebhookObserved",
                schema: "conversations",
                table: "messages");

            migrationBuilder.CreateIndex(
                name: "IX_messages_ConversationId",
                schema: "conversations",
                table: "messages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_messages_ProviderMessageId",
                schema: "conversations",
                table: "messages",
                column: "ProviderMessageId",
                unique: true,
                filter: "\"ProviderMessageId\" IS NOT NULL");
        }
    }
}
