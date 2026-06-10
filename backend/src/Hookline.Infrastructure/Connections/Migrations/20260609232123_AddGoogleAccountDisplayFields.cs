using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hookline.Infrastructure.Connections.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleAccountDisplayFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "channel_id",
                schema: "connections",
                table: "google_accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "account_email",
                schema: "connections",
                table: "google_accounts",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avatar_url",
                schema: "connections",
                table: "google_accounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_google_accounts_channel_id",
                schema: "connections",
                table: "google_accounts",
                column: "channel_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_google_accounts_channel_id",
                schema: "connections",
                table: "google_accounts");

            migrationBuilder.DropColumn(
                name: "account_email",
                schema: "connections",
                table: "google_accounts");

            migrationBuilder.DropColumn(
                name: "avatar_url",
                schema: "connections",
                table: "google_accounts");

            migrationBuilder.AlterColumn<string>(
                name: "channel_id",
                schema: "connections",
                table: "google_accounts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
