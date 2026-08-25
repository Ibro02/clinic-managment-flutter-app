using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: 1 (not EF's auto-generated 0) - Location Id 0 doesn't
            // exist, so a default of 0 would make the AddForeignKey below fail for
            // any Doctor row not covered by the explicit UpdateData calls right
            // after this (there shouldn't be any beyond the two seeded doctors,
            // but a default that can't violate the FK it's about to create is the
            // correct safety net regardless, not a "should be fine" assumption).
            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "Doctors",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.UpdateData(
                table: "Doctors",
                keyColumn: "Id",
                keyValue: 1,
                column: "LocationId",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Doctors",
                keyColumn: "Id",
                keyValue: 2,
                column: "LocationId",
                value: 2);

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_LocationId",
                table: "Doctors",
                column: "LocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Doctors_Locations_LocationId",
                table: "Doctors",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Doctors_Locations_LocationId",
                table: "Doctors");

            migrationBuilder.DropIndex(
                name: "IX_Doctors_LocationId",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Doctors");
        }
    }
}
