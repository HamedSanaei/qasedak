using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectionEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountType",
                schema: "instagram",
                table: "connected_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "instagram",
                table: "connected_accounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSubscriptionCheckUtc",
                schema: "instagram",
                table: "connected_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastTokenIssuedAtUtc",
                schema: "instagram",
                table: "connected_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfilePictureUrl",
                schema: "instagram",
                table: "connected_accounts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProfileUpdatedAtUtc",
                schema: "instagram",
                table: "connected_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionDetail",
                schema: "instagram",
                table: "connected_accounts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionHealth",
                schema: "instagram",
                table: "connected_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                schema: "instagram",
                table: "connected_accounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                schema: "instagram",
                table: "connected_accounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "oauth_states",
                schema: "instagram",
                columns: table => new
                {
                    StateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RedirectUri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oauth_states", x => x.StateHash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_oauth_states_ExpiresAtUtc",
                schema: "instagram",
                table: "oauth_states",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_oauth_states_StateHash",
                schema: "instagram",
                table: "oauth_states",
                column: "StateHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oauth_states",
                schema: "instagram");

            migrationBuilder.DropColumn(
                name: "AccountType",
                schema: "instagram",
                table: "connected_accounts");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "instagram",
                table: "connected_accounts");

            migrationBuilder.DropColumn(
                name: "LastSubscriptionCheckUtc",
                schema: "instagram",
                table: "connected_accounts");

            migrationBuilder.DropColumn(
                name: "LastTokenIssuedAtUtc",
                schema: "instagram",
                table: "connected_accounts");

            migrationBuilder.DropColumn(
                name: "ProfilePictureUrl",
                schema: "instagram",
                table: "connected_accounts");

            migrationBuilder.DropColumn(
                name: "ProfileUpdatedAtUtc",
                schema: "instagram",
                table: "connected_accounts");

            migrationBuilder.DropColumn(
                name: "SubscriptionDetail",
                schema: "instagram",
                table: "connected_accounts");

            migrationBuilder.DropColumn(
                name: "SubscriptionHealth",
                schema: "instagram",
                table: "connected_accounts");

            migrationBuilder.DropColumn(
                name: "Username",
                schema: "instagram",
                table: "connected_accounts");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "instagram",
                table: "connected_accounts");
        }
    }
}
