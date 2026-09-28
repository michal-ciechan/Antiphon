using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Antiphon.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelOutboundPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelOutboundPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromptSequence = table.Column<long>(type: "bigint", nullable: false),
                    FirstTextSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastTextSequence = table.Column<long>(type: "bigint", nullable: false),
                    Path = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConversationId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ReplyHandle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OriginalResponse = table.Column<string>(type: "text", nullable: false),
                    EnvelopeJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    AttemptOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailure = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FailureStage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MetadataStampedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BundleTaskIdsJson = table.Column<string>(type: "text", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelOutboundPublications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelOutboundPublications_AgentSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AgentSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelOutboundPublicationSources",
                columns: table => new
                {
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FirstTextSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastTextSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelOutboundPublicationSources", x => new { x.PublicationId, x.QueueMessageId });
                    table.ForeignKey(
                        name: "FK_ChannelOutboundPublicationSources_ChannelOutboundPublicatio~",
                        column: x => x.PublicationId,
                        principalTable: "ChannelOutboundPublications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelOutboundPublicationSources_SessionQueuedMessages_Que~",
                        column: x => x.QueueMessageId,
                        principalTable: "SessionQueuedMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundPublications_IntervalTarget",
                table: "ChannelOutboundPublications",
                columns: new[] { "SessionId", "PromptSequence", "FirstTextSequence", "LastTextSequence", "Path", "Provider", "ConversationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundPublications_Recovery",
                table: "ChannelOutboundPublications",
                columns: new[] { "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundPublicationSources_MainMachineOwner",
                table: "ChannelOutboundPublicationSources",
                columns: new[] { "QueueMessageId", "Path" },
                unique: true,
                filter: "\"Path\" <> 'trailing'");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelOutboundPublicationSources_SourceInterval",
                table: "ChannelOutboundPublicationSources",
                columns: new[] { "QueueMessageId", "Path", "FirstTextSequence", "LastTextSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelOutboundPublicationSources");

            migrationBuilder.DropTable(
                name: "ChannelOutboundPublications");
        }
    }
}
