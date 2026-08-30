using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class ScopeTeacherRestrictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TeacherId",
                table: "RestrictionRules",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestrictionRules_TeacherId",
                table: "RestrictionRules",
                column: "TeacherId");

            migrationBuilder.AddForeignKey(
                name: "FK_RestrictionRules_Teachers_TeacherId",
                table: "RestrictionRules",
                column: "TeacherId",
                principalTable: "Teachers",
                principalColumn: "TeacherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RestrictionRules_Teachers_TeacherId",
                table: "RestrictionRules");

            migrationBuilder.DropIndex(
                name: "IX_RestrictionRules_TeacherId",
                table: "RestrictionRules");

            migrationBuilder.DropColumn(
                name: "TeacherId",
                table: "RestrictionRules");
        }
    }
}
