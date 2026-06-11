using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookline.Modules.YouTubeUploads.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelMappingIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "youtube_uploads",
                table: "channel_mappings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "youtube_uploads",
                table: "channel_mappings");
        }
    }
}
