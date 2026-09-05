using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveRoutingIdentityIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_connected_accounts_active_routing_identity",
                schema: "instagram",
                table: "connected_accounts",
                column: "ProviderUserId",
                filter: "\"DisconnectedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_connected_accounts_active_routing_identity",
                schema: "instagram",
                table: "connected_accounts");
        }
    }
}
