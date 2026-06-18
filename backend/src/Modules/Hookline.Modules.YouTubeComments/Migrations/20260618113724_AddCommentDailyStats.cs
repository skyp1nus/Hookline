using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookline.Modules.YouTubeComments.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentDailyStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comment_daily_stats",
                schema: "youtube_comments",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    mapping_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    forwarded = table.Column<int>(type: "integer", nullable: false),
                    replies = table.Column<int>(type: "integer", nullable: false),
                    removed = table.Column<int>(type: "integer", nullable: false),
                    rejected = table.Column<int>(type: "integer", nullable: false),
                    already_gone = table.Column<int>(type: "integer", nullable: false),
                    sum_likes = table.Column<long>(type: "bigint", nullable: false),
                    enriched_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comment_daily_stats", x => new { x.date, x.mapping_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_comment_daily_stats_date",
                schema: "youtube_comments",
                table: "comment_daily_stats",
                column: "date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comment_daily_stats",
                schema: "youtube_comments");
        }
    }
}
