using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class CompleteRemainingScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "Teachers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEndUtc",
                table: "Teachers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "Students",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEndUtc",
                table: "Students",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PcName",
                table: "RemoteCommandLogs",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StudentId",
                table: "RemoteCommandLogs",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DedupeKey",
                table: "MonitoringAlerts",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "Admins",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Admins",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEndUtc",
                table: "Admins",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SessionRules",
                keyColumn: "SessionRuleId",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 8, 29, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.CreateIndex(
                name: "IX_RemoteCommandLogs_TeacherId_StudentId_Timestamp",
                table: "RemoteCommandLogs",
                columns: new[] { "TeacherId", "StudentId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringAlerts_StudentId_DedupeKey_CreatedAt",
                table: "MonitoringAlerts",
                columns: new[] { "StudentId", "DedupeKey", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RemoteCommandLogs_TeacherId_StudentId_Timestamp",
                table: "RemoteCommandLogs");

            migrationBuilder.DropIndex(
                name: "IX_MonitoringAlerts_StudentId_DedupeKey_CreatedAt",
                table: "MonitoringAlerts");

            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "LockoutEndUtc",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "LockoutEndUtc",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "PcName",
                table: "RemoteCommandLogs");

            migrationBuilder.DropColumn(
                name: "StudentId",
                table: "RemoteCommandLogs");

            migrationBuilder.DropColumn(
                name: "DedupeKey",
                table: "MonitoringAlerts");

            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "Admins");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Admins");

            migrationBuilder.DropColumn(
                name: "LockoutEndUtc",
                table: "Admins");

            migrationBuilder.UpdateData(
                table: "SessionRules",
                keyColumn: "SessionRuleId",
                keyValue: 1,
                column: "CreatedAt",
                value: new DateTime(2026, 8, 30, 1, 57, 29, 59, DateTimeKind.Local).AddTicks(5031));
        }
    }
}
