using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCapturedAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CapturedAmountEur",
                table: "Payments",
                type: "decimal(8,2)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Payments",
                keyColumn: "Id",
                keyValue: 1,
                column: "CapturedAmountEur",
                value: 25.56m);

            migrationBuilder.UpdateData(
                table: "Payments",
                keyColumn: "Id",
                keyValue: 2,
                column: "CapturedAmountEur",
                value: 40.90m);

            migrationBuilder.UpdateData(
                table: "Payments",
                keyColumn: "Id",
                keyValue: 3,
                column: "CapturedAmountEur",
                value: 46.02m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CapturedAmountEur",
                table: "Payments");
        }
    }
}
