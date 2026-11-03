using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUzbekCyrillic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name_cyrl",
                table: "regions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "message_cyrl",
                table: "notifications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "message_ru",
                table: "notifications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name_cyrl",
                table: "districts",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "name_cyrl",
                table: "regions");

            migrationBuilder.DropColumn(
                name: "message_cyrl",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "message_ru",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "name_cyrl",
                table: "districts");
        }
    }
}
