namespace ClinicNow.Model.Configuration;

/// <summary>
/// Whether the Scalar/OpenAPI interactive docs are exposed, sourced from <c>.env</c>
/// like every other option group (rulebook Part II §C).
///
/// Deliberately independent of <see cref="Microsoft.Extensions.Hosting.IHostEnvironment.IsDevelopment"/>:
/// the shipped container runs as Production (see docker-compose.yml) so
/// <c>ExceptionFilter</c> never leaks a stack trace, but the reference repo this
/// project mirrors still exposes Swagger to the reviewer in that same Production
/// container - it is API documentation, not a dev/test endpoint the rulebook §5
/// asks to be gated behind an environment check.
/// </summary>
public class ApiDocsOptions : EnvOptionsBase
{
    public bool Enabled { get; }

    public ApiDocsOptions()
    {
        Enabled = GetOrDefault("ENABLE_API_DOCS", true);
    }
}
