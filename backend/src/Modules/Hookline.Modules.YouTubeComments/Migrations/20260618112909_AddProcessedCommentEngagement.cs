using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookline.Modules.YouTubeComments.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedCommentEngagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "author_channel_url",
                schema: "youtube_comments",
                table: "processed_comments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "author_name",
                schema: "youtube_comments",
                table: "processed_comments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "comment_length",
                schema: "youtube_comments",
                table: "processed_comments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "like_count",
                schema: "youtube_comments",
                table: "processed_comments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                schema: "youtube_comments",
                table: "processed_comments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "video_title",
                schema: "youtube_comments",
                table: "processed_comments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "author_channel_url",
                schema: "youtube_comments",
                table: "processed_comments");

            migrationBuilder.DropColumn(
                name: "author_name",
                schema: "youtube_comments",
                table: "processed_comments");

            migrationBuilder.DropColumn(
                name: "comment_length",
                schema: "youtube_comments",
                table: "processed_comments");

            migrationBuilder.DropColumn(
                name: "like_count",
                schema: "youtube_comments",
                table: "processed_comments");

            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "youtube_comments",
                table: "processed_comments");

            migrationBuilder.DropColumn(
                name: "video_title",
                schema: "youtube_comments",
                table: "processed_comments");
        }
    }
}
