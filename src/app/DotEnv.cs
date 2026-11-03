namespace SmartReport.App;

/// <summary>
/// Loads <c>.env</c> into environment variables for local development.
///
/// <para>
/// Keeps <c>appsettings.json</c> free of credentials (C2) while leaving <c>dotnet run</c> working
/// straight after <c>cp .env.example .env</c>. Existing environment variables always win, so a
/// real deployment's configuration is never overwritten by a stray file.
/// </para>
/// </summary>
public static class DotEnv
{
    public static void LoadIfPresent(string directory)
    {
        var path = Path.Combine(directory, ".env");
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim().Trim('"');

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    /// <summary>Builds a connection string from the discrete variables in .env.</summary>
    public static string? BuildConnectionString(string userVariable, string passwordVariable)
    {
        var user = Environment.GetEnvironmentVariable(userVariable);
        var password = Environment.GetEnvironmentVariable(passwordVariable);
        var database = Environment.GetEnvironmentVariable("POSTGRES_DB");
        var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(database))
        {
            return null;
        }

        return $"Host=localhost;Port={port};Database={database};Username={user};Password={password}";
    }
}
