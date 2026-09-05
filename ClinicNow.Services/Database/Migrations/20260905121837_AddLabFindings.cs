using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLabFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LabFindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    AppointmentId = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    EnteredByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabFindings_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LabFindings_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LabFindings_Users_EnteredByUserId",
                        column: x => x.EnteredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LabFindings",
                columns: new[] { "Id", "AppointmentId", "ContentType", "CreatedAtUtc", "DeletedAtUtc", "EnteredByUserId", "FileData", "FileName", "FileSizeBytes", "IsDeleted", "PatientId", "Result" },
                values: new object[] { 1, 1, "application/pdf", new DateTime(2026, 8, 18, 9, 30, 0, 0, DateTimeKind.Utc), null, 3, new byte[] { 37, 80, 68, 70, 45, 49, 46, 52, 10, 49, 32, 48, 32, 111, 98, 106, 60, 60, 47, 84, 121, 112, 101, 47, 67, 97, 116, 97, 108, 111, 103, 47, 80, 97, 103, 101, 115, 32, 50, 32, 48, 32, 82, 62, 62, 101, 110, 100, 111, 98, 106, 10, 50, 32, 48, 32, 111, 98, 106, 60, 60, 47, 84, 121, 112, 101, 47, 80, 97, 103, 101, 115, 47, 75, 105, 100, 115, 91, 51, 32, 48, 32, 82, 93, 47, 67, 111, 117, 110, 116, 32, 49, 62, 62, 101, 110, 100, 111, 98, 106, 10, 51, 32, 48, 32, 111, 98, 106, 60, 60, 47, 84, 121, 112, 101, 47, 80, 97, 103, 101, 47, 80, 97, 114, 101, 110, 116, 32, 50, 32, 48, 32, 82, 47, 77, 101, 100, 105, 97, 66, 111, 120, 91, 48, 32, 48, 32, 50, 48, 48, 32, 50, 48, 48, 93, 62, 62, 101, 110, 100, 111, 98, 106, 10, 120, 114, 101, 102, 10, 48, 32, 52, 10, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 32, 54, 53, 53, 51, 53, 32, 102, 32, 10, 116, 114, 97, 105, 108, 101, 114, 60, 60, 47, 83, 105, 122, 101, 32, 52, 47, 82, 111, 111, 116, 32, 49, 32, 48, 32, 82, 62, 62, 10, 115, 116, 97, 114, 116, 120, 114, 101, 102, 10, 48, 10, 37, 37, 69, 79, 70 }, "nalaz-kks.pdf", 240L, false, 1, "Kompletna krvna slika - uredni parametri." });

            migrationBuilder.CreateIndex(
                name: "IX_LabFindings_AppointmentId",
                table: "LabFindings",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_LabFindings_EnteredByUserId",
                table: "LabFindings",
                column: "EnteredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LabFindings_PatientId",
                table: "LabFindings",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LabFindings");
        }
    }
}
