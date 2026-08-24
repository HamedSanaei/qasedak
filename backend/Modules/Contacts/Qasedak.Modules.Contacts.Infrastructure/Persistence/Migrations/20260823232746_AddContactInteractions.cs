using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Contacts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_interactions",
                schema: "contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_interactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contact_interactions_EventId",
                schema: "contacts",
                table: "contact_interactions",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contact_interactions_WorkspaceId_OccurredAtUtc",
                schema: "contacts",
                table: "contact_interactions",
                columns: new[] { "WorkspaceId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_interactions",
                schema: "contacts");
        }
    }
}
