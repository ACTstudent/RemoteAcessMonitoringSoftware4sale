using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddBrowserMonitoringHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrowserMonitoringRecords",
                columns: table => new
                {
                    BrowserMonitoringRecordId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConnectionId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StudentId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PcName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Browser = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrowserMonitoringRecords", x => x.BrowserMonitoringRecordId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrowserMonitoringRecords_PcName_Timestamp",
                table: "BrowserMonitoringRecords",
                columns: new[] { "PcName", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_BrowserMonitoringRecords_StudentId_Timestamp",
                table: "BrowserMonitoringRecords",
                columns: new[] { "StudentId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrowserMonitoringRecords");
        }
    }
}
