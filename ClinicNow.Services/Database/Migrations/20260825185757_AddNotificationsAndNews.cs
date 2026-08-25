using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsAndNews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAtUtc",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NewsItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ImageData = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ImageContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Appointments",
                keyColumn: "Id",
                keyValue: 1,
                column: "ReminderSentAtUtc",
                value: null);

            migrationBuilder.UpdateData(
                table: "Appointments",
                keyColumn: "Id",
                keyValue: 2,
                column: "ReminderSentAtUtc",
                value: null);

            migrationBuilder.UpdateData(
                table: "Appointments",
                keyColumn: "Id",
                keyValue: 3,
                column: "ReminderSentAtUtc",
                value: null);

            migrationBuilder.UpdateData(
                table: "Appointments",
                keyColumn: "Id",
                keyValue: 4,
                column: "ReminderSentAtUtc",
                value: null);

            migrationBuilder.UpdateData(
                table: "Appointments",
                keyColumn: "Id",
                keyValue: 5,
                column: "ReminderSentAtUtc",
                value: null);

            migrationBuilder.InsertData(
                table: "NewsItems",
                columns: new[] { "Id", "CreatedAtUtc", "ImageContentType", "ImageData", "Text", "Title" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 8, 15, 8, 0, 0, 0, DateTimeKind.Utc), "image/png", new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1, 8, 4, 0, 0, 0, 181, 28, 12, 2, 0, 0, 0, 11, 73, 68, 65, 84, 120, 218, 99, 100, 248, 15, 0, 1, 5, 1, 1, 39, 24, 227, 102, 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }, "Poliklinika Sunce proširuje radno vrijeme kardiologije od 1. septembra.", "Nove ordinacije od septembra" },
                    { 2, new DateTime(2026, 8, 20, 12, 0, 0, 0, DateTimeKind.Utc), null, null, "Sada možete zakazati termin direktno iz mobilne aplikacije, bez poziva.", "Online zakazivanje termina" },
                    { 3, new DateTime(2026, 8, 23, 15, 0, 0, 0, DateTimeKind.Utc), "image/png", new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1, 8, 4, 0, 0, 0, 181, 28, 12, 2, 0, 0, 0, 11, 73, 68, 65, 84, 120, 218, 99, 100, 248, 15, 0, 1, 5, 1, 1, 39, 24, 227, 102, 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }, "Preporučujemo redovne godišnje preglede - zakažite svoj termin na vrijeme.", "Sezonski pregledi" }
                });

            migrationBuilder.InsertData(
                table: "Notifications",
                columns: new[] { "Id", "CreatedAtUtc", "IsRead", "ReadAtUtc", "Text", "Title", "UserId" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 8, 20, 9, 0, 0, 0, DateTimeKind.Utc), false, null, "Vaš nalog je uspješno kreiran. Zakažite svoj prvi termin iz aplikacije.", "Dobrodošli u ClinicNow", 4 },
                    { 2, new DateTime(2026, 8, 21, 10, 30, 0, 0, DateTimeKind.Utc), true, new DateTime(2026, 8, 21, 11, 0, 0, 0, DateTimeKind.Utc), "Pacijent Amina Selimović je zakazao/la termin za pregled.", "Novi termin zakazan", 3 },
                    { 3, new DateTime(2026, 8, 24, 8, 0, 0, 0, DateTimeKind.Utc), false, null, "Provjerite raspored za sutra - nekoliko termina čeka potvrdu.", "Podsjetnik: nadolazeći termini", 2 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewsItems");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropColumn(
                name: "ReminderSentAtUtc",
                table: "Appointments");
        }
    }
}
