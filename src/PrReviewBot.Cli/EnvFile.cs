namespace PrReviewBot.Cli;

/// <summary>
/// Loads local development secrets/config from a ".env" file (KEY=VALUE per line) into the
/// process environment, so all the values Program.cs reads via Environment.GetEnvironmentVariable
/// have one place to live locally. Real environment variables (e.g. secrets injected by GitHub
/// Actions) always take precedence and are never overwritten.
/// </summary>
internal static class EnvFile
{
    public static void Load(string fileName = ".env")
    {
        var path = FindUpwards(fileName);
        if (path is null)
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? FindUpwards(string fileName)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var depth = 0; depth < 6 && directory is not null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
