using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderSyncOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_sync_operations",
                schema: "instagram",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    ConversationsObserved = table.Column<int>(type: "integer", nullable: false),
                    MessageIdsObserved = table.Column<int>(type: "integer", nullable: false),
                    DetailsFetched = table.Column<int>(type: "integer", nullable: false),
                    MessagesImported = table.Column<int>(type: "integer", nullable: false),
                    Duplicates = table.Column<int>(type: "integer", nullable: false),
                    Unsupported = table.Column<int>(type: "integer", nullable: false),
                    HistoryWindowLimited = table.Column<int>(type: "integer", nullable: false),
                    RateLimited = table.Column<int>(type: "integer", nullable: false),
                    FailureCategory = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    NextProviderCursor = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_sync_operations", x => x.OperationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provider_sync_operations_ConnectedAccountId_CreatedAtUtc",
                schema: "instagram",
                table: "provider_sync_operations",
                columns: new[] { "ConnectedAccountId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_provider_sync_operations_one_active_per_account_kind",
                schema: "instagram",
                table: "provider_sync_operations",
                columns: new[] { "ConnectedAccountId", "Kind" },
                unique: true,
                filter: "\"Status\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_sync_operations",
                schema: "instagram");
        }
    }
}
