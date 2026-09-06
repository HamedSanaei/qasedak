using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comment_effects",
                schema: "instagram",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCommentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EffectType = table.Column<int>(type: "integer", nullable: false),
                    OwnerOperationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderRecipientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comment_effects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_comment_effects_ConnectedAccountId_ProviderCommentId_Effect~",
                schema: "instagram",
                table: "comment_effects",
                columns: new[] { "ConnectedAccountId", "ProviderCommentId", "EffectType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comment_effects",
                schema: "instagram");
        }
    }
}
