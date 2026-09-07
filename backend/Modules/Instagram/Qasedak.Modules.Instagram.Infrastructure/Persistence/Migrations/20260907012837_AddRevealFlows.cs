using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRevealFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reveal_flows",
                schema: "instagram",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCommentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ParticipantIGSID = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsLiveComment = table.Column<bool>(type: "boolean", nullable: false),
                    NotificationOccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AutomationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AutomationVersionNumber = table.Column<int>(type: "integer", nullable: true),
                    TriggerEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ActionIndex = table.Column<int>(type: "integer", nullable: true),
                    OpeningPrivateReplyText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    GatePromptText = table.Column<string>(type: "character varying(640)", maxLength: 640, nullable: false),
                    PostbackButtonTitle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FollowUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FollowButtonTitle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RevealText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FollowGateMode = table.Column<int>(type: "integer", nullable: false),
                    OpeningPrivateReplyMessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OpeningPrivateReplyRecipientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    LastUserMessageAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GatePromptStatus = table.Column<int>(type: "integer", nullable: false),
                    GatePromptProviderMessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    GatePromptAttemptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GatePromptFailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CorrelationTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CorrelationTokenPurpose = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    RevealStatus = table.Column<int>(type: "integer", nullable: false),
                    RevealProviderMessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    RevealAttemptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevealCompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevealFailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastFollowState = table.Column<int>(type: "integer", nullable: true),
                    LastFollowUnavailableReason = table.Column<int>(type: "integer", nullable: true),
                    LastFollowCheckAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reveal_flows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reveal_flows_ConnectedAccountId_ParticipantIGSID_State",
                schema: "instagram",
                table: "reveal_flows",
                columns: new[] { "ConnectedAccountId", "ParticipantIGSID", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_reveal_flows_ConnectedAccountId_ProviderCommentId",
                schema: "instagram",
                table: "reveal_flows",
                columns: new[] { "ConnectedAccountId", "ProviderCommentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reveal_flows_CorrelationTokenHash",
                schema: "instagram",
                table: "reveal_flows",
                column: "CorrelationTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reveal_flows_OpeningPrivateReplyMessageId",
                schema: "instagram",
                table: "reveal_flows",
                column: "OpeningPrivateReplyMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reveal_flows",
                schema: "instagram");
        }
    }
}
