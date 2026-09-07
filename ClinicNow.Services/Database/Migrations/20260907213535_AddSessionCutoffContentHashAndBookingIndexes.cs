using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionCutoffContentHashAndBookingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkingHoursEntries_DoctorId",
                table: "WorkingHoursEntries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleBlocks_DoctorId",
                table: "ScheduleBlocks");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_PatientId",
                table: "Appointments");

            migrationBuilder.AddColumn<DateTime>(
                name: "TokensValidFromUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "NewsItems",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "MedicalDocuments",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "LabFindings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "LabFindings",
                keyColumn: "Id",
                keyValue: 1,
                column: "ContentHash",
                value: "BD84E762D65BA7554EFD671A9AEDA45F");

            migrationBuilder.UpdateData(
                table: "MedicalDocuments",
                keyColumn: "Id",
                keyValue: 1,
                column: "ContentHash",
                value: "BD84E762D65BA7554EFD671A9AEDA45F");

            migrationBuilder.UpdateData(
                table: "NewsItems",
                keyColumn: "Id",
                keyValue: 1,
                column: "ContentHash",
                value: "431CED6916A2A21A156E38701AFE55BB");

            migrationBuilder.UpdateData(
                table: "NewsItems",
                keyColumn: "Id",
                keyValue: 2,
                column: "ContentHash",
                value: null);

            migrationBuilder.UpdateData(
                table: "NewsItems",
                keyColumn: "Id",
                keyValue: 3,
                column: "ContentHash",
                value: "431CED6916A2A21A156E38701AFE55BB");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "TokensValidFromUtc",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                column: "TokensValidFromUtc",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                column: "TokensValidFromUtc",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                column: "TokensValidFromUtc",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 5,
                column: "TokensValidFromUtc",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_WorkingHoursEntries_DoctorId_DayOfWeek",
                table: "WorkingHoursEntries",
                columns: new[] { "DoctorId", "DayOfWeek" })
                .Annotation("SqlServer:Include", new[] { "StartTime", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleBlocks_DoctorId_StartUtc",
                table: "ScheduleBlocks",
                columns: new[] { "DoctorId", "StartUtc" })
                .Annotation("SqlServer:Include", new[] { "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorId_StartUtc",
                table: "Appointments",
                columns: new[] { "DoctorId", "StartUtc" })
                .Annotation("SqlServer:Include", new[] { "EndUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PatientId_StartUtc",
                table: "Appointments",
                columns: new[] { "PatientId", "StartUtc" })
                .Annotation("SqlServer:Include", new[] { "EndUtc", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkingHoursEntries_DoctorId_DayOfWeek",
                table: "WorkingHoursEntries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleBlocks_DoctorId_StartUtc",
                table: "ScheduleBlocks");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorId_StartUtc",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_PatientId_StartUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "TokensValidFromUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "NewsItems");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "MedicalDocuments");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "LabFindings");

            migrationBuilder.CreateIndex(
                name: "IX_WorkingHoursEntries_DoctorId",
                table: "WorkingHoursEntries",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleBlocks_DoctorId",
                table: "ScheduleBlocks",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorId",
                table: "Appointments",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PatientId",
                table: "Appointments",
                column: "PatientId");
        }
    }
}
