using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookline.Infrastructure.Connections.Migrations
{
    /// <inheritdoc />
    public partial class AddSlackWorkspaceApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_slack_workspaces_team_id",
                schema: "connections",
                table: "slack_workspaces");

            migrationBuilder.AddColumn<string>(
                name: "app",
                schema: "connections",
                table: "slack_workspaces",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            // Backfill: any pre-existing workspace row was written by the YouTube Uploads Slack app (the
            // only app whose OAuth populated the shared store before per-app rows existed), so its stored
            // bot token belongs to that app. Stamp it so Uploads keeps resolving its token without re-OAuth;
            // the Comments app then installs into its own (team_id, 'youtube-comments') row.
            migrationBuilder.Sql(
                "UPDATE connections.slack_workspaces SET app = 'youtube-uploads' WHERE app = '';");

            migrationBuilder.CreateIndex(
                name: "ix_slack_workspaces_team_id_app",
                schema: "connections",
                table: "slack_workspaces",
                columns: new[] { "team_id", "app" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_slack_workspaces_team_id_app",
                schema: "connections",
                table: "slack_workspaces");

            migrationBuilder.DropColumn(
                name: "app",
                schema: "connections",
                table: "slack_workspaces");

            migrationBuilder.CreateIndex(
                name: "ix_slack_workspaces_team_id",
                schema: "connections",
                table: "slack_workspaces",
                column: "team_id",
                unique: true);
        }
    }
}
