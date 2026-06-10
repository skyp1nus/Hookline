using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hookline.Modules.YouTubeUploads.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "youtube_uploads");

            migrationBuilder.CreateTable(
                name: "channel_mappings",
                schema: "youtube_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slack_workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slack_channel_id = table.Column<string>(type: "text", nullable: false),
                    slack_channel_name = table.Column<string>(type: "text", nullable: false),
                    google_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channel_mappings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "google_projects",
                schema: "youtube_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    encrypted_client_secret = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_google_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "slack_channels",
                schema: "youtube_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slack_channel_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_private = table.Column<bool>(type: "boolean", nullable: false),
                    is_member = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_slack_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "upload_jobs",
                schema: "youtube_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slack_event_id = table.Column<string>(type: "text", nullable: false),
                    slack_channel_id = table.Column<string>(type: "text", nullable: false),
                    slack_user_id = table.Column<string>(type: "text", nullable: false),
                    slack_message_ts = table.Column<string>(type: "text", nullable: false),
                    drive_file_id = table.Column<string>(type: "text", nullable: false),
                    google_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    tags = table.Column<string>(type: "jsonb", nullable: false),
                    thumbnail_url = table.Column<string>(type: "text", nullable: true),
                    thumbnail_mime_type = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    requires_confirmation = table.Column<bool>(type: "boolean", nullable: false),
                    confirmed = table.Column<bool>(type: "boolean", nullable: true),
                    bytes_total = table.Column<long>(type: "bigint", nullable: false),
                    bytes_transferred = table.Column<long>(type: "bigint", nullable: false),
                    original_file_name = table.Column<string>(type: "text", nullable: true),
                    download_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    upload_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    you_tube_video_id = table.Column<string>(type: "text", nullable: true),
                    you_tube_url = table.Column<string>(type: "text", nullable: true),
                    quota_units_charged = table.Column<int>(type: "integer", nullable: false),
                    hangfire_job_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "google_account_bindings",
                schema: "youtube_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    you_tube_channel_id = table.Column<string>(type: "text", nullable: true),
                    label = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_google_account_bindings", x => x.id);
                    table.ForeignKey(
                        name: "fk_google_account_bindings_google_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "youtube_uploads",
                        principalTable: "google_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_state_history",
                schema: "youtube_uploads",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_state_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_state_history_upload_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "youtube_uploads",
                        principalTable: "upload_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_channel_mappings_slack_channel_id",
                schema: "youtube_uploads",
                table: "channel_mappings",
                column: "slack_channel_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_google_account_bindings_account_id",
                schema: "youtube_uploads",
                table: "google_account_bindings",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_google_account_bindings_project_id",
                schema: "youtube_uploads",
                table: "google_account_bindings",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_google_account_bindings_you_tube_channel_id",
                schema: "youtube_uploads",
                table: "google_account_bindings",
                column: "you_tube_channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_google_projects_client_id",
                schema: "youtube_uploads",
                table: "google_projects",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_state_history_job_id",
                schema: "youtube_uploads",
                table: "job_state_history",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_slack_channels_slack_channel_id",
                schema: "youtube_uploads",
                table: "slack_channels",
                column: "slack_channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_slack_channels_workspace_id_slack_channel_id",
                schema: "youtube_uploads",
                table: "slack_channels",
                columns: new[] { "workspace_id", "slack_channel_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_upload_jobs_created_at",
                schema: "youtube_uploads",
                table: "upload_jobs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_upload_jobs_slack_channel_id_updated_at",
                schema: "youtube_uploads",
                table: "upload_jobs",
                columns: new[] { "slack_channel_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_upload_jobs_slack_event_id",
                schema: "youtube_uploads",
                table: "upload_jobs",
                column: "slack_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_jobs_state",
                schema: "youtube_uploads",
                table: "upload_jobs",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_mappings",
                schema: "youtube_uploads");

            migrationBuilder.DropTable(
                name: "google_account_bindings",
                schema: "youtube_uploads");

            migrationBuilder.DropTable(
                name: "job_state_history",
                schema: "youtube_uploads");

            migrationBuilder.DropTable(
                name: "slack_channels",
                schema: "youtube_uploads");

            migrationBuilder.DropTable(
                name: "google_projects",
                schema: "youtube_uploads");

            migrationBuilder.DropTable(
                name: "upload_jobs",
                schema: "youtube_uploads");
        }
    }
}
