using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Contacts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialContactsCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "contacts");

            migrationBuilder.CreateTable(
                name: "contacts",
                schema: "contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InteractionCount = table.Column<long>(type: "bigint", nullable: false),
                    MergedIntoId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contact_identities",
                schema: "contacts",
                columns: table => new
                {
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_identities", x => new { x.ContactId, x.Channel, x.ProviderIdentity });
                    table.ForeignKey(
                        name: "FK_contact_identities_contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "contacts",
                        principalTable: "contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contact_identities_WorkspaceId_Channel_ProviderIdentity",
                schema: "contacts",
                table: "contact_identities",
                columns: new[] { "WorkspaceId", "Channel", "ProviderIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contacts_WorkspaceId_Status_LastSeenAtUtc",
                schema: "contacts",
                table: "contacts",
                columns: new[] { "WorkspaceId", "Status", "LastSeenAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_identities",
                schema: "contacts");

            migrationBuilder.DropTable(
                name: "contacts",
                schema: "contacts");
        }
    }
}
