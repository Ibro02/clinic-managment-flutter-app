using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosisCodebook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-ordered, not as scaffolded. EF emitted the DropColumn and the
            // NOT NULL AddColumn *before* the Diagnoses table existed, which would
            // leave every pre-existing entry pointing at id 0 and then fail the
            // foreign key at the end of this migration. The order below creates
            // and fills the codebook first, migrates the old free-text values onto
            // it, and only then drops the old column and adds the constraint.
            migrationBuilder.CreateTable(
                name: "Diagnoses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SuggestedSpecializationId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Diagnoses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Diagnoses_Specializations_SuggestedSpecializationId",
                        column: x => x.SuggestedSpecializationId,
                        principalTable: "Specializations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Diagnoses",
                columns: new[] { "Id", "Code", "Name", "SuggestedSpecializationId" },
                values: new object[,]
                {
                    { 1, "Z00.0", "Opća kontrola bez nalaza", 1 },
                    { 2, "J06.9", "Akutna infekcija gornjih disajnih puteva", 1 },
                    { 3, "I10", "Esencijalna (primarna) hipertenzija", 4 },
                    { 4, "I48", "Atrijalna fibrilacija i treperenje", 4 },
                    { 5, "L20.9", "Atopijski dermatitis", 2 },
                    { 6, "L70.0", "Acne vulgaris", 2 },
                    { 7, "J45.9", "Astma", 3 },
                    { 8, "N94.6", "Dismenoreja", 5 },
                    { 9, "E11.9", "Dijabetes melitus tip 2", 1 },
                    { 10, "R51", "Glavobolja", 1 },
                    { 11, "Z00.1", "Rutinska pedijatrijska kontrola", 3 },
                    { 12, "E03.9", "Hipotireoza", 1 },
                    { 13, "I25.9", "Hronična ishemijska bolest srca", 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Diagnoses_Code",
                table: "Diagnoses",
                column: "Code",
                unique: true);

            migrationBuilder.AddColumn<int>(
                name: "DiagnosisId",
                table: "MedicalRecordEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DiagnosisNote",
                table: "MedicalRecordEntries",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            // Migrate the old free text onto the codebook. Existing rows were
            // written as "<code> - <name>", so the code is everything before the
            // first " - " and matches a Diagnoses.Code directly.
            migrationBuilder.Sql(@"
                UPDATE e
                SET e.[DiagnosisId] = d.[Id]
                FROM [MedicalRecordEntries] e
                JOIN [Diagnoses] d
                  ON d.[Code] = LEFT(e.[Diagnosis], NULLIF(CHARINDEX(' - ', e.[Diagnosis]), 0) - 1)");

            // Anything the join could not place (a diagnosis typed as free text
            // that never matched a code) keeps its original wording in
            // DiagnosisNote and falls back to Z00.0 - so no history is lost and
            // the foreign key below can be added.
            migrationBuilder.Sql(@"
                UPDATE [MedicalRecordEntries]
                SET [DiagnosisNote] = [Diagnosis], [DiagnosisId] = 1
                WHERE [DiagnosisId] = 0");

            migrationBuilder.DropColumn(
                name: "Diagnosis",
                table: "MedicalRecordEntries");

            migrationBuilder.UpdateData(
                table: "MedicalRecordEntries",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DiagnosisId", "DiagnosisNote" },
                values: new object[] { 1, null });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalRecordEntries_DiagnosisId",
                table: "MedicalRecordEntries",
                column: "DiagnosisId");

            migrationBuilder.CreateIndex(
                name: "IX_Diagnoses_SuggestedSpecializationId",
                table: "Diagnoses",
                column: "SuggestedSpecializationId");

            migrationBuilder.AddForeignKey(
                name: "FK_MedicalRecordEntries_Diagnoses_DiagnosisId",
                table: "MedicalRecordEntries",
                column: "DiagnosisId",
                principalTable: "Diagnoses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MedicalRecordEntries_Diagnoses_DiagnosisId",
                table: "MedicalRecordEntries");

            migrationBuilder.DropTable(
                name: "Diagnoses");

            migrationBuilder.DropIndex(
                name: "IX_MedicalRecordEntries_DiagnosisId",
                table: "MedicalRecordEntries");

            migrationBuilder.DropColumn(
                name: "DiagnosisId",
                table: "MedicalRecordEntries");

            migrationBuilder.DropColumn(
                name: "DiagnosisNote",
                table: "MedicalRecordEntries");

            migrationBuilder.AddColumn<string>(
                name: "Diagnosis",
                table: "MedicalRecordEntries",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "MedicalRecordEntries",
                keyColumn: "Id",
                keyValue: 1,
                column: "Diagnosis",
                value: "Z00.0 - Opća kontrola bez nalaza");
        }
    }
}
