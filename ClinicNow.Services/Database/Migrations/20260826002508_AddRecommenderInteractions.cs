using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommenderInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecommenderInteractions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    InteractionType = table.Column<int>(type: "int", nullable: false),
                    DoctorId = table.Column<int>(type: "int", nullable: true),
                    MedicalServiceId = table.Column<int>(type: "int", nullable: true),
                    DateTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommenderInteractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommenderInteractions_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecommenderInteractions_MedicalServices_MedicalServiceId",
                        column: x => x.MedicalServiceId,
                        principalTable: "MedicalServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecommenderInteractions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RecommenderInteractions",
                columns: new[] { "Id", "DateTimeUtc", "DoctorId", "InteractionType", "MedicalServiceId", "UserId" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 8, 22, 9, 15, 0, 0, DateTimeKind.Utc), 1, 0, null, 4 },
                    { 2, new DateTime(2026, 8, 22, 9, 16, 0, 0, DateTimeKind.Utc), null, 1, 1, 4 },
                    { 3, new DateTime(2026, 8, 23, 18, 40, 0, 0, DateTimeKind.Utc), 2, 2, null, 4 },
                    { 4, new DateTime(2026, 8, 24, 8, 5, 0, 0, DateTimeKind.Utc), 1, 0, null, 4 },
                    { 5, new DateTime(2026, 8, 21, 14, 0, 0, 0, DateTimeKind.Utc), null, 1, 2, 4 },
                    { 6, new DateTime(2026, 8, 24, 19, 30, 0, 0, DateTimeKind.Utc), null, 2, 5, 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommenderInteractions_DateTimeUtc",
                table: "RecommenderInteractions",
                column: "DateTimeUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RecommenderInteractions_DoctorId",
                table: "RecommenderInteractions",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_RecommenderInteractions_MedicalServiceId",
                table: "RecommenderInteractions",
                column: "MedicalServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecommenderInteractions_UserId",
                table: "RecommenderInteractions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommenderInteractions");
        }
    }
}
