using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicNow.Services.Database.Migrations
{
    /// <inheritdoc />
    public partial class BackfillMissingMedicalRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Migration AddMedicalRecords only seeded a MedicalRecord for the two
            // HasData-seeded patients - any Patient row created at runtime before
            // this feature existed (e.g. a self-registered account) has none.
            // "Every patient has exactly one medical file" must hold for *all*
            // data, not just the fixed seed, so back-fill an empty record for
            // every patient that's still missing one.
            migrationBuilder.Sql(
                """
                INSERT INTO [MedicalRecords] ([PatientId], [CreatedAtUtc], [UpdatedAtUtc])
                SELECT [p].[Id], SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM [Patients] AS [p]
                LEFT JOIN [MedicalRecords] AS [mr] ON [mr].[PatientId] = [p].[Id]
                WHERE [mr].[Id] IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data backfill - deliberately not reversed (Down would have to
            // guess which records were backfilled vs. genuinely created since).
        }
    }
}
