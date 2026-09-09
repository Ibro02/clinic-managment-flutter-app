using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralTargetDoctor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TargetDoctorId",
                table: "Referrals",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Referrals",
                keyColumn: "Id",
                keyValue: 1,
                column: "TargetDoctorId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_TargetDoctorId",
                table: "Referrals",
                column: "TargetDoctorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Doctors_TargetDoctorId",
                table: "Referrals",
                column: "TargetDoctorId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Doctors_TargetDoctorId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_TargetDoctorId",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "TargetDoctorId",
                table: "Referrals");
        }
    }
}
