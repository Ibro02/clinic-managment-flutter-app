using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppointmentId = table.Column<int>(type: "int", nullable: false),
                    AmountEur = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PayPalOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayPalCaptureId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    MedicalServiceId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AmountEur = table.Column<decimal>(type: "decimal(8,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentItems_MedicalServices_MedicalServiceId",
                        column: x => x.MedicalServiceId,
                        principalTable: "MedicalServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentItems_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    AmountEur = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    PayPalRefundId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RefundedByUserId = table.Column<int>(type: "int", nullable: false),
                    RefundedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentRefunds_Users_RefundedByUserId",
                        column: x => x.RefundedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Payments",
                columns: new[] { "Id", "AmountEur", "AppointmentId", "CreatedAtUtc", "PaidAtUtc", "PayPalCaptureId", "PayPalOrderId", "Status" },
                values: new object[,]
                {
                    { 1, 25.56m, 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "SEED-CAPTURE-0001", "SEED-ORDER-0001", 1 },
                    { 2, 40.90m, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "SEED-CAPTURE-0002", "SEED-ORDER-0002", 3 },
                    { 3, 46.02m, 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "SEED-CAPTURE-0003", "SEED-ORDER-0003", 2 }
                });

            migrationBuilder.InsertData(
                table: "PaymentItems",
                columns: new[] { "Id", "AmountEur", "Description", "MedicalServiceId", "PaymentId" },
                values: new object[,]
                {
                    { 1, 25.56m, "Opći pregled", 1, 1 },
                    { 2, 40.90m, "Dermatološki pregled", 2, 2 },
                    { 3, 46.02m, "Kardiološki pregled", 5, 3 }
                });

            migrationBuilder.InsertData(
                table: "PaymentRefunds",
                columns: new[] { "Id", "AmountEur", "PayPalRefundId", "PaymentId", "Reason", "RefundedAtUtc", "RefundedByUserId" },
                values: new object[,]
                {
                    { 1, 40.90m, "SEED-REFUND-0001", 2, "Termin otkazan.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2 },
                    { 2, 15.00m, "SEED-REFUND-0002", 3, "Djelomični povrat na zahtjev pacijenta.", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentItems_MedicalServiceId",
                table: "PaymentItems",
                column: "MedicalServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentItems_PaymentId",
                table: "PaymentItems",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_PaymentId",
                table: "PaymentRefunds",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRefunds_RefundedByUserId",
                table: "PaymentRefunds",
                column: "RefundedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_AppointmentId",
                table: "Payments",
                column: "AppointmentId",
                unique: true,
                filter: "[Status] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentItems");

            migrationBuilder.DropTable(
                name: "PaymentRefunds");

            migrationBuilder.DropTable(
                name: "Payments");
        }
    }
}
