using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookline.Modules.YouTubeComments.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "youtube_comments");

            migrationBuilder.CreateTable(
                name: "quota_usage",
                schema: "youtube_comments",
                columns: table => new
                {
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usage_date = table.Column<DateOnly>(type: "date", nullable: false),
                    units_used = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quota_usage", x => new { x.api_key_id, x.usage_date });
                });

            migrationBuilder.CreateTable(
                name: "slack_channels",
                schema: "youtube_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slack_channel_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_private = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slack_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "youtube_channels",
                schema: "youtube_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    youtube_channel_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    thumbnail_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    handle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_youtube_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "channel_mappings",
                schema: "youtube_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    youtube_channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slack_channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    include_replies = table.Column<bool>(type: "boolean", nullable: false),
                    reply_sweep_frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reply_window_days = table.Column<int>(type: "integer", nullable: false),
                    comments_since_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_channel_mappings_slack_channels_slack_channel_id",
                        column: x => x.slack_channel_id,
                        principalSchema: "youtube_comments",
                        principalTable: "slack_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_channel_mappings_you_tube_channels_you_tube_channel_id",
                        column: x => x.youtube_channel_id,
                        principalSchema: "youtube_comments",
                        principalTable: "youtube_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_deliveries",
                schema: "youtube_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mapping_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parent_comment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    video_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_pending_deliveries_channel_mappings_mapping_id",
                        column: x => x.mapping_id,
                        principalSchema: "youtube_comments",
                        principalTable: "channel_mappings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "processed_comments",
                schema: "youtube_comments",
                columns: table => new
                {
                    mapping_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    video_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    slack_message_ts = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    parent_comment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_comments", x => new { x.mapping_id, x.comment_id });
                    table.ForeignKey(
                        name: "fk_processed_comments_channel_mappings_mapping_id",
                        column: x => x.mapping_id,
                        principalSchema: "youtube_comments",
                        principalTable: "channel_mappings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_mappings_slack_channel_id",
                schema: "youtube_comments",
                table: "channel_mappings",
                column: "slack_channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_mappings_youtube_channel_id_slack_channel_id",
                schema: "youtube_comments",
                table: "channel_mappings",
                columns: new[] { "youtube_channel_id", "slack_channel_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_deliveries_mapping_id",
                schema: "youtube_comments",
                table: "pending_deliveries",
                column: "mapping_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_deliveries_next_attempt_at",
                schema: "youtube_comments",
                table: "pending_deliveries",
                column: "next_attempt_at");

            migrationBuilder.CreateIndex(
                name: "ix_processed_comments_processed_at",
                schema: "youtube_comments",
                table: "processed_comments",
                column: "processed_at");

            migrationBuilder.CreateIndex(
                name: "ix_slack_channels_workspace_id_slack_channel_id",
                schema: "youtube_comments",
                table: "slack_channels",
                columns: new[] { "workspace_id", "slack_channel_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_youtube_channels_youtube_channel_id",
                schema: "youtube_comments",
                table: "youtube_channels",
                column: "youtube_channel_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_deliveries",
                schema: "youtube_comments");

            migrationBuilder.DropTable(
                name: "processed_comments",
                schema: "youtube_comments");

            migrationBuilder.DropTable(
                name: "quota_usage",
                schema: "youtube_comments");

            migrationBuilder.DropTable(
                name: "channel_mappings",
                schema: "youtube_comments");

            migrationBuilder.DropTable(
                name: "slack_channels",
                schema: "youtube_comments");

            migrationBuilder.DropTable(
                name: "youtube_channels",
                schema: "youtube_comments");
        }
    }
}
