using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralResultingAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResultingAppointmentId",
                table: "Referrals",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Referrals",
                keyColumn: "Id",
                keyValue: 1,
                column: "ResultingAppointmentId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ResultingAppointmentId",
                table: "Referrals",
                column: "ResultingAppointmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Appointments_ResultingAppointmentId",
                table: "Referrals",
                column: "ResultingAppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Appointments_ResultingAppointmentId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_ResultingAppointmentId",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "ResultingAppointmentId",
                table: "Referrals");
        }
    }
}
