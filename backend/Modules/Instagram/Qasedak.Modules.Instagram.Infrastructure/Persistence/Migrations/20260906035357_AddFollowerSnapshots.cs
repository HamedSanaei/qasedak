using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowerSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "follower_snapshots",
                schema: "instagram",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    FollowerCount = table.Column<long>(type: "bigint", nullable: false),
                    Provenance = table.Column<int>(type: "integer", nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_follower_snapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_follower_snapshots_ConnectedAccountId_SnapshotDateUtc",
                schema: "instagram",
                table: "follower_snapshots",
                columns: new[] { "ConnectedAccountId", "SnapshotDateUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "follower_snapshots",
                schema: "instagram");
        }
    }
}
