using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddReferralSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Referrals",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Referrals",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Referrals",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DeletedAtUtc", "IsDeleted" },
                values: new object[] { null, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Referrals");
        }
    }
}
