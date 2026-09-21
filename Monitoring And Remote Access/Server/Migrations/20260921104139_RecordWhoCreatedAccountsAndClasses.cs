using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class RecordWhoCreatedAccountsAndClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreatedById",
                table: "Teachers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByType",
                table: "Teachers",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "System");

            migrationBuilder.AddColumn<int>(
                name: "CreatedById",
                table: "Students",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByType",
                table: "Students",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "System");

            migrationBuilder.AddColumn<int>(
                name: "CreatedById",
                table: "Classes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByType",
                table: "Classes",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "System");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "CreatedByType",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "CreatedByType",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "Classes");

            migrationBuilder.DropColumn(
                name: "CreatedByType",
                table: "Classes");
        }
    }
}
