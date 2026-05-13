using System.IO;

namespace UnityMcpLens.CommandCenter;

public sealed class CommandLineOptions
{
    public string ProjectRoot { get; init; } = Directory.GetCurrentDirectory();
    public string PackageRoot { get; init; } = string.Empty;
    public string StatusDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".unity",
        "mcp",
        "connections");
    public int UnityPid { get; init; }

    public static CommandLineOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                values[arg] = args[++i];
            else
                values[arg] = "true";
        }

        values.TryGetValue("--project-root", out string? projectRoot);
        values.TryGetValue("--package-root", out string? packageRoot);
        values.TryGetValue("--status-dir", out string? statusDir);
        values.TryGetValue("--unity-pid", out string? unityPidText);

        return new CommandLineOptions
        {
            ProjectRoot = NormalizeOrDefault(projectRoot, Directory.GetCurrentDirectory()),
            PackageRoot = NormalizeOrDefault(packageRoot, string.Empty),
            StatusDirectory = NormalizeOrDefault(statusDir, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".unity",
                "mcp",
                "connections")),
            UnityPid = int.TryParse(unityPidText, out int unityPid) ? unityPid : 0
        };
    }

    static string NormalizeOrDefault(string? path, string fallback)
    {
        if (string.IsNullOrWhiteSpace(path))
            return fallback;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
