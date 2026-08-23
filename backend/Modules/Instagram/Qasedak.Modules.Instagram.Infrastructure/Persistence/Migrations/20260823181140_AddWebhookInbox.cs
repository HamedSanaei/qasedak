using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_inbox",
                schema: "instagram",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Topic = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BodyJson = table.Column<string>(type: "text", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveryAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_inbox", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_inbox_Status_ReceivedAtUtc",
                schema: "instagram",
                table: "webhook_inbox",
                columns: new[] { "Status", "ReceivedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_inbox",
                schema: "instagram");
        }
    }
}
