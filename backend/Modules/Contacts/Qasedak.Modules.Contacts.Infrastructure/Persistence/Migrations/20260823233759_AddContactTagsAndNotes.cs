using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Contacts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactTagsAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_notes",
                schema: "contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contact_notes_contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "contacts",
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_tags",
                schema: "contacts",
                columns: table => new
                {
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_tags", x => new { x.ContactId, x.Tag });
                    table.ForeignKey(
                        name: "FK_contact_tags_contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "contacts",
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contact_notes_ContactId",
                schema: "contacts",
                table: "contact_notes",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_contact_notes_WorkspaceId_CreatedAtUtc",
                schema: "contacts",
                table: "contact_notes",
                columns: new[] { "WorkspaceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_contact_tags_WorkspaceId_Tag",
                schema: "contacts",
                table: "contact_tags",
                columns: new[] { "WorkspaceId", "Tag" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_notes",
                schema: "contacts");

            migrationBuilder.DropTable(
                name: "contact_tags",
                schema: "contacts");
        }
    }
}
