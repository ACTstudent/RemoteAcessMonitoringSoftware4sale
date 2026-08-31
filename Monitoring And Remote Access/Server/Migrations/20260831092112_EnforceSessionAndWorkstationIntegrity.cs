using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSessionAndWorkstationIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LabSessions_ComputerId",
                table: "LabSessions");

            migrationBuilder.Sql("""
                UPDATE "LabSessions"
                SET "IsActive" = 0,
                    "Status" = 'Ended',
                    "EndTime" = COALESCE("EndTime", CURRENT_TIMESTAMP)
                WHERE "IsActive" = 1
                  AND "Id" NOT IN (
                      SELECT MAX("Id") FROM "LabSessions" WHERE "IsActive" = 1 GROUP BY "StudentId"
                  );

                UPDATE "LabSessions"
                SET "IsActive" = 0,
                    "Status" = 'Ended',
                    "EndTime" = COALESCE("EndTime", CURRENT_TIMESTAMP)
                WHERE "IsActive" = 1
                  AND "ComputerId" IS NOT NULL
                  AND "Id" NOT IN (
                      SELECT MAX("Id") FROM "LabSessions"
                      WHERE "IsActive" = 1 AND "ComputerId" IS NOT NULL
                      GROUP BY "ComputerId"
                  );

                UPDATE "Computers"
                SET "AssignedTo" = NULL,
                    "Status" = CASE WHEN "Status" = 'Assigned' THEN 'Available' ELSE "Status" END
                WHERE "AssignedTo" IS NOT NULL
                  AND "ComputerId" NOT IN (
                      SELECT MIN("ComputerId") FROM "Computers"
                      WHERE "AssignedTo" IS NOT NULL
                      GROUP BY "AssignedTo"
                  );

                UPDATE "Computers" AS duplicate
                SET "LaboratoryStation" = duplicate."LaboratoryStation" || '-' || duplicate."ComputerId"
                WHERE EXISTS (
                    SELECT 1 FROM "Computers" AS original
                    WHERE original."ComputerId" < duplicate."ComputerId"
                      AND LOWER(original."LaboratoryStation") = LOWER(duplicate."LaboratoryStation")
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_LabSessions_ComputerId",
                table: "LabSessions",
                column: "ComputerId",
                unique: true,
                filter: "\"IsActive\" = 1 AND \"ComputerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LabSessions_StudentId",
                table: "LabSessions",
                column: "StudentId",
                unique: true,
                filter: "\"IsActive\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Computers_AssignedTo",
                table: "Computers",
                column: "AssignedTo",
                unique: true,
                filter: "\"AssignedTo\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Computers_LaboratoryStation",
                table: "Computers",
                column: "LaboratoryStation",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LabSessions_ComputerId",
                table: "LabSessions");

            migrationBuilder.DropIndex(
                name: "IX_LabSessions_StudentId",
                table: "LabSessions");

            migrationBuilder.DropIndex(
                name: "IX_Computers_AssignedTo",
                table: "Computers");

            migrationBuilder.DropIndex(
                name: "IX_Computers_LaboratoryStation",
                table: "Computers");

            migrationBuilder.CreateIndex(
                name: "IX_LabSessions_ComputerId",
                table: "LabSessions",
                column: "ComputerId");
        }
    }
}
