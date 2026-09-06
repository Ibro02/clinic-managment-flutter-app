using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRefundFailureState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RefundFailedAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundFailureReason",
                table: "Payments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Payments",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "RefundFailedAtUtc", "RefundFailureReason" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Payments",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "RefundFailedAtUtc", "RefundFailureReason" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Payments",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "RefundFailedAtUtc", "RefundFailureReason" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundFailedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundFailureReason",
                table: "Payments");
        }
    }
}
