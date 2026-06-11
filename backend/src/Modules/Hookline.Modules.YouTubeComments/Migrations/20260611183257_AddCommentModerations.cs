using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookline.Modules.YouTubeComments.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentModerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comment_moderations",
                schema: "youtube_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mapping_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    slack_user_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    slack_user_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comment_moderations", x => x.id);
                    table.ForeignKey(
                        name: "fk_comment_moderations_channel_mappings_mapping_id",
                        column: x => x.mapping_id,
                        principalSchema: "youtube_comments",
                        principalTable: "channel_mappings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comment_moderations_mapping_id_comment_id",
                schema: "youtube_comments",
                table: "comment_moderations",
                columns: new[] { "mapping_id", "comment_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comment_moderations",
                schema: "youtube_comments");
        }
    }
}
