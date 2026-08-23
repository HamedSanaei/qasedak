using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInstagramCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "instagram");

            migrationBuilder.CreateTable(
                name: "account_tokens",
                schema: "instagram",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ciphertext = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_tokens", x => x.AccountId);
                });

            migrationBuilder.CreateTable(
                name: "connected_accounts",
                schema: "instagram",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Path = table.Column<int>(type: "integer", nullable: false),
                    Scopes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Health = table.Column<int>(type: "integer", nullable: false),
                    HealthDetail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TokenExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConnectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DisconnectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connected_accounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_connected_accounts_WorkspaceId_ProviderUserId",
                schema: "instagram",
                table: "connected_accounts",
                columns: new[] { "WorkspaceId", "ProviderUserId" },
                unique: true,
                filter: "\"DisconnectedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_tokens",
                schema: "instagram");

            migrationBuilder.DropTable(
                name: "connected_accounts",
                schema: "instagram");
        }
    }
}
