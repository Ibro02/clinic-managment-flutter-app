namespace ClinicNow.Model.Configuration;

/// <summary>SQL Server connection string, sourced from <c>.env</c> (key: <c>DB_CONNECTION_STRING</c>).</summary>
public class DatabaseOptions : EnvOptionsBase
{
    public string ConnectionString { get; }

    public DatabaseOptions()
    {
        ConnectionString = Require("DB_CONNECTION_STRING");
    }
}
