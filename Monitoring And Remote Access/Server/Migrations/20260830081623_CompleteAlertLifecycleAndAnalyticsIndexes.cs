using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class CompleteAlertLifecycleAndAnalyticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LabSessions_StudentId",
                table: "LabSessions");

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAt",
                table: "MonitoringAlerts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcknowledgedByTeacherId",
                table: "MonitoringAlerts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DismissalReason",
                table: "MonitoringAlerts",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DismissedAt",
                table: "MonitoringAlerts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DismissedByTeacherId",
                table: "MonitoringAlerts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstSeenAt",
                table: "MonitoringAlerts",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "GroupKey",
                table: "MonitoringAlerts",
                type: "TEXT",
                maxLength: 350,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "MonitoringAlerts",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "OccurrenceCount",
                table: "MonitoringAlerts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                """
                UPDATE MonitoringAlerts
                SET FirstSeenAt = CreatedAt,
                    LastSeenAt = CreatedAt,
                    OccurrenceCount = CASE WHEN OccurrenceCount < 1 THEN 1 ELSE OccurrenceCount END,
                    GroupKey = lower(trim(StudentId)) || '|' || lower(trim(PcName)) || '|' ||
                        lower(trim(CASE WHEN DedupeKey = '' THEN Title ELSE DedupeKey END));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringAlerts_StudentId_GroupKey_LastSeenAt",
                table: "MonitoringAlerts",
                columns: new[] { "StudentId", "GroupKey", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LabSessions_StudentId_StartTime",
                table: "LabSessions",
                columns: new[] { "StudentId", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_IdleIntervals_StudentId_StartedAt",
                table: "IdleIntervals",
                columns: new[] { "StudentId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_StudentId_Timestamp",
                table: "ActivityEvents",
                columns: new[] { "StudentId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonitoringAlerts_StudentId_GroupKey_LastSeenAt",
                table: "MonitoringAlerts");

            migrationBuilder.DropIndex(
                name: "IX_LabSessions_StudentId_StartTime",
                table: "LabSessions");

            migrationBuilder.DropIndex(
                name: "IX_IdleIntervals_StudentId_StartedAt",
                table: "IdleIntervals");

            migrationBuilder.DropIndex(
                name: "IX_ActivityEvents_StudentId_Timestamp",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                table: "MonitoringAlerts");

            migrationBuilder.DropColumn(
                name: "AcknowledgedByTeacherId",
                table: "MonitoringAlerts");

            migrationBuilder.DropColumn(
                name: "DismissalReason",
                table: "MonitoringAlerts");

            migrationBuilder.DropColumn(
                name: "DismissedAt",
                table: "MonitoringAlerts");

            migrationBuilder.DropColumn(
                name: "DismissedByTeacherId",
                table: "MonitoringAlerts");

            migrationBuilder.DropColumn(
                name: "FirstSeenAt",
                table: "MonitoringAlerts");

            migrationBuilder.DropColumn(
                name: "GroupKey",
                table: "MonitoringAlerts");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "MonitoringAlerts");

            migrationBuilder.DropColumn(
                name: "OccurrenceCount",
                table: "MonitoringAlerts");

            migrationBuilder.CreateIndex(
                name: "IX_LabSessions_StudentId",
                table: "LabSessions",
                column: "StudentId");
        }
    }
}
