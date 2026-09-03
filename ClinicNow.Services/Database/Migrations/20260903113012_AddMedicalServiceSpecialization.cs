using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalServiceSpecialization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue is 1 ("Opća medicina"), not EF's scaffolded 0. The five
            // seeded services get their real specialization from the UpdateData
            // calls below, but any service an administrator created before this
            // migration has no value at all - and 0 matches no Specialization row,
            // so AddForeignKey at the end of this migration would fail and abort
            // the upgrade. Landing those rows on a real, general specialization
            // keeps the migration applicable to a database that has been used.
            migrationBuilder.AddColumn<int>(
                name: "SpecializationId",
                table: "MedicalServices",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.InsertData(
                table: "DoctorSpecializations",
                columns: new[] { "DoctorId", "SpecializationId" },
                values: new object[,]
                {
                    { 1, 2 },
                    { 2, 1 }
                });

            migrationBuilder.UpdateData(
                table: "MedicalServices",
                keyColumn: "Id",
                keyValue: 1,
                column: "SpecializationId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "MedicalServices",
                keyColumn: "Id",
                keyValue: 2,
                column: "SpecializationId",
                value: 2);

            migrationBuilder.UpdateData(
                table: "MedicalServices",
                keyColumn: "Id",
                keyValue: 3,
                column: "SpecializationId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "MedicalServices",
                keyColumn: "Id",
                keyValue: 4,
                column: "SpecializationId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "MedicalServices",
                keyColumn: "Id",
                keyValue: 5,
                column: "SpecializationId",
                value: 4);

            migrationBuilder.CreateIndex(
                name: "IX_MedicalServices_SpecializationId",
                table: "MedicalServices",
                column: "SpecializationId");

            migrationBuilder.AddForeignKey(
                name: "FK_MedicalServices_Specializations_SpecializationId",
                table: "MedicalServices",
                column: "SpecializationId",
                principalTable: "Specializations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MedicalServices_Specializations_SpecializationId",
                table: "MedicalServices");

            migrationBuilder.DropIndex(
                name: "IX_MedicalServices_SpecializationId",
                table: "MedicalServices");

            migrationBuilder.DeleteData(
                table: "DoctorSpecializations",
                keyColumns: new[] { "DoctorId", "SpecializationId" },
                keyValues: new object[] { 1, 2 });

            migrationBuilder.DeleteData(
                table: "DoctorSpecializations",
                keyColumns: new[] { "DoctorId", "SpecializationId" },
                keyValues: new object[] { 2, 1 });

            migrationBuilder.DropColumn(
                name: "SpecializationId",
                table: "MedicalServices");
        }
    }
}
