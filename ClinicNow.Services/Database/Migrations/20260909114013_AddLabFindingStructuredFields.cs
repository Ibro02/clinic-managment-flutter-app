using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLabFindingStructuredFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "LabFindings",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(260)",
                oldMaxLength: 260);

            migrationBuilder.AlterColumn<byte[]>(
                name: "FileData",
                table: "LabFindings",
                type: "varbinary(max)",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "varbinary(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                table: "LabFindings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<string>(
                name: "DoctorNote",
                table: "LabFindings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceRange",
                table: "LabFindings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestName",
                table: "LabFindings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "LabFindings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Value",
                table: "LabFindings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // TestName arrives NOT NULL with an empty default, which would leave
            // any finding written before this migration unnamed. Those rows only
            // ever had the free-text Result, so it becomes the test name (capped
            // at the column width); the seeded row is overwritten with its real
            // value by the UpdateData below.
            migrationBuilder.Sql(
                "UPDATE [LabFindings] SET [TestName] = LEFT([Result], 200) WHERE [TestName] = ''");

            migrationBuilder.UpdateData(
                table: "LabFindings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DoctorNote", "ReferenceRange", "TestName", "Unit", "Value" },
                values: new object[] { "Nalaz uredan, kontrola nije potrebna.", "12.0 - 16.0", "Kompletna krvna slika (KKS) - hemoglobin", "g/dL", "13.9" });

            migrationBuilder.CreateIndex(
                name: "IX_LabFindings_TestName",
                table: "LabFindings",
                column: "TestName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LabFindings_TestName",
                table: "LabFindings");

            migrationBuilder.DropColumn(
                name: "DoctorNote",
                table: "LabFindings");

            migrationBuilder.DropColumn(
                name: "ReferenceRange",
                table: "LabFindings");

            migrationBuilder.DropColumn(
                name: "TestName",
                table: "LabFindings");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "LabFindings");

            migrationBuilder.DropColumn(
                name: "Value",
                table: "LabFindings");

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "LabFindings",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(260)",
                oldMaxLength: 260,
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "FileData",
                table: "LabFindings",
                type: "varbinary(max)",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "varbinary(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                table: "LabFindings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
