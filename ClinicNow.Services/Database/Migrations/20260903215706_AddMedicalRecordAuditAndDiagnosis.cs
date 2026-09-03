using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalRecordAuditAndDiagnosis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "MedicalRecordEntries",
                type: "datetime2",
                nullable: true);

            // defaultValue is a clear placeholder, not an empty string - any
            // treatment-history row that predates this migration (created before
            // Diagnosis existed) should surface as visibly needing review rather
            // than silently rendering a blank required field. Deliberate edit,
            // same reasoning as the AddMedicalServiceSpecialization migration
            // (review item C2).
            migrationBuilder.AddColumn<string>(
                name: "Diagnosis",
                table: "MedicalRecordEntries",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "Nije evidentirano");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "MedicalRecordEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MedicalRecordAuditLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MedicalRecordId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    ActingUserId = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalRecordAuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalRecordAuditLog_MedicalRecords_MedicalRecordId",
                        column: x => x.MedicalRecordId,
                        principalTable: "MedicalRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MedicalRecordAuditLog_Users_ActingUserId",
                        column: x => x.ActingUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "MedicalRecordAuditLog",
                columns: new[] { "Id", "ActingUserId", "Action", "Description", "MedicalRecordId", "OccurredAtUtc" },
                values: new object[] { 1, 3, 2, "Unos dodan: Redovni pregled.", 1, new DateTime(2026, 8, 10, 9, 30, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "MedicalRecordEntries",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DeletedAtUtc", "Diagnosis", "IsDeleted" },
                values: new object[] { null, "Z00.0 - Opća kontrola bez nalaza", false });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalRecordAuditLog_ActingUserId",
                table: "MedicalRecordAuditLog",
                column: "ActingUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalRecordAuditLog_MedicalRecordId",
                table: "MedicalRecordAuditLog",
                column: "MedicalRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicalRecordAuditLog");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "MedicalRecordEntries");

            migrationBuilder.DropColumn(
                name: "Diagnosis",
                table: "MedicalRecordEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "MedicalRecordEntries");
        }
    }
}
