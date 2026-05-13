using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace UnityMcpLens.CommandCenter;

public sealed class InstallerService
{
    const string MetadataFileName = "unity-mcp-lens.json";
    const string InstalledServerFileName = "unity_mcp_lens_win.exe";
    const string CommandCenterFileName = "unity_mcp_lens_command_center_win.exe";

    readonly string m_PackageRoot;

    public string InstalledDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".unity",
        "unity-mcp-lens");

    public string InstalledServerPath => Path.Combine(InstalledDirectory, InstalledServerFileName);
    public string InstalledMetadataPath => Path.Combine(InstalledDirectory, MetadataFileName);

    public InstallerService(string packageRoot)
    {
        m_PackageRoot = packageRoot;
    }

    public ServerInstallSnapshot GetSnapshot()
    {
        string installedVersion = ReadVersion(InstalledMetadataPath);
        string bundledVersion = ReadVersion(Path.Combine(m_PackageRoot, MetadataFileName));
        return new ServerInstallSnapshot
        {
            InstalledDirectory = InstalledDirectory,
            InstalledServerPath = InstalledServerPath,
            InstalledServerExists = File.Exists(InstalledServerPath),
            InstalledVersion = installedVersion,
            BundledVersion = bundledVersion,
            CommandCenterUpdateAvailable = IsCommandCenterUpdateAvailable()
        };
    }

    public string RefreshServer()
    {
        if (string.IsNullOrWhiteSpace(m_PackageRoot) || !Directory.Exists(m_PackageRoot))
            throw new InvalidOperationException("Package root was not provided or no longer exists.");

        string staging = Path.Combine(Path.GetTempPath(), $"unity-mcp-lens-server-refresh-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            PublishServer(staging);
            Directory.CreateDirectory(InstalledDirectory);
            CopyDirectoryContents(staging, InstalledDirectory, skipCommandCenter: true);

            string metadataSource = Path.Combine(m_PackageRoot, MetadataFileName);
            if (File.Exists(metadataSource))
                File.Copy(metadataSource, InstalledMetadataPath, true);

            return $"Server refreshed at {DateTime.Now:t}.";
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    void PublishServer(string staging)
    {
        string runtimeId = GetRuntimeIdentifier();
        string prebuiltDirectory = Path.Combine(m_PackageRoot, "prebuilt", runtimeId);
        string prebuiltServer = Path.Combine(prebuiltDirectory, InstalledServerFileName);
        if (File.Exists(prebuiltServer))
        {
            File.Copy(prebuiltServer, Path.Combine(staging, InstalledServerFileName), true);
            return;
        }

        string projectFile = Path.Combine(m_PackageRoot, "src", "UnityMcpLens", "UnityMcpLens.csproj");
        if (!File.Exists(projectFile))
            throw new FileNotFoundException("Unity MCP Lens server project file was not found.", projectFile);

        string dotnet = ResolveDotNetExecutable();
        string arguments =
            $"publish \"{projectFile}\" -c Release -r {runtimeId} --self-contained true /p:PublishSingleFile=true /p:DebugType=None /p:DebugSymbols=false -o \"{staging}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = dotnet,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectFile)
        };

        using Process? process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start dotnet publish for the Unity MCP Lens server.");

        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet publish failed with exit {process.ExitCode}.\n{standardOutput}\n{standardError}".Trim());

        string publishedDefault = Path.Combine(staging, "UnityMcpLens.exe");
        string expected = Path.Combine(staging, InstalledServerFileName);
        if (File.Exists(publishedDefault) && !string.Equals(publishedDefault, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(publishedDefault, expected, true);
            File.Delete(publishedDefault);
        }
    }

    bool IsCommandCenterUpdateAvailable()
    {
        string currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe) || string.IsNullOrWhiteSpace(m_PackageRoot))
            return false;

        DateTime currentWriteUtc = File.GetLastWriteTimeUtc(currentExe);
        string runtimeId = GetRuntimeIdentifier();
        string prebuiltPath = Path.Combine(m_PackageRoot, "prebuilt", runtimeId, CommandCenterFileName);
        if (File.Exists(prebuiltPath) && File.GetLastWriteTimeUtc(prebuiltPath) > currentWriteUtc.AddSeconds(1))
            return true;

        string sourceDirectory = Path.Combine(m_PackageRoot, "src", "UnityMcpLens.CommandCenter");
        DateTime sourceNewest = GetNewestWriteUtc(sourceDirectory);
        return sourceNewest > currentWriteUtc.AddSeconds(1);
    }

    static string ResolveDotNetExecutable()
    {
        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            string candidate = Path.Combine(dotnetRoot, "dotnet.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return "dotnet.exe";
    }

    static string GetRuntimeIdentifier()
    {
        return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
    }

    static void CopyDirectoryContents(string sourceDir, string targetDir, bool skipCommandCenter)
    {
        foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(targetDir, relativePath));
        }

        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            if (skipCommandCenter && string.Equals(Path.GetFileName(file), CommandCenterFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            string relativePath = Path.GetRelativePath(sourceDir, file);
            string destination = Path.Combine(targetDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? targetDir);
            File.Copy(file, destination, true);
        }
    }

    static DateTime GetNewestWriteUtc(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return DateTime.MinValue;

        DateTime newest = DateTime.MinValue;
        foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DateTime writeUtc = File.GetLastWriteTimeUtc(file);
            if (writeUtc > newest)
                newest = writeUtc;
        }

        return newest;
    }

    static string ReadVersion(string metadataPath)
    {
        try
        {
            if (!File.Exists(metadataPath))
                return "not installed";

            JsonObject? json = JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject;
            return json != null &&
                json.TryGetPropertyValue("version", out JsonNode? versionNode) &&
                versionNode != null
                ? versionNode.GetValue<string>()
                : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}

public sealed class ServerInstallSnapshot
{
    public string InstalledDirectory { get; init; } = string.Empty;
    public string InstalledServerPath { get; init; } = string.Empty;
    public bool InstalledServerExists { get; init; }
    public string InstalledVersion { get; init; } = "unknown";
    public string BundledVersion { get; init; } = "unknown";
    public bool CommandCenterUpdateAvailable { get; init; }
}
