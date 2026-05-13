using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnityMcpLens.CommandCenter;

public sealed class ProjectSettingsStore
{
    const string PackageName = "com.becool3000.unity-mcp-lens";
    const string McpSettingsKey = "McpSettings";

    readonly JsonSerializerOptions m_WriteOptions = new() { WriteIndented = true };

    public string SettingsPath { get; }

    public ProjectSettingsStore(string projectRoot)
    {
        SettingsPath = Path.Combine(
            projectRoot,
            "ProjectSettings",
            "Packages",
            PackageName,
            "Settings.json");
    }

    public LensSettingsSnapshot Load()
    {
        JsonObject raw = ReadRaw();
        JsonObject mcp = GetOrCreateObject(raw, McpSettingsKey);
        JsonObject policies = GetOrCreateObject(mcp, "connectionPolicies");
        JsonObject direct = GetOrCreateObject(policies, "direct");

        return new LensSettingsSnapshot
        {
            BridgeEnabled = GetBool(mcp, "bridgeEnabled", true),
            BatchModeEnabled = GetBool(mcp, "batchModeEnabled", true),
            AutoApproveInBatchMode = GetBool(mcp, "autoApproveInBatchMode", true),
            ProcessValidationEnabled = GetBool(mcp, "processValidationEnabled", false),
            LegacyRelayEnabled = GetBool(raw, "LegacyRelayEnabled", false),
            ValidationLevel = GetString(mcp, "validationLevel", "standard"),
            MaxDirectConnections = GetInt(policies, "maxDirectConnections", -1),
            EnabledToolOverrides = GetStringArray(mcp, "enabledToolOverrides"),
            DisabledToolOverrides = GetStringArray(mcp, "disabledToolOverrides")
        };
    }

    public void Save(LensSettingsSnapshot settings)
    {
        JsonObject raw = ReadRaw();
        raw["LegacyRelayEnabled"] = settings.LegacyRelayEnabled;

        JsonObject mcp = GetOrCreateObject(raw, McpSettingsKey);
        mcp["bridgeEnabled"] = settings.BridgeEnabled;
        mcp["batchModeEnabled"] = settings.BatchModeEnabled;
        mcp["autoApproveInBatchMode"] = settings.AutoApproveInBatchMode;
        mcp["validationLevel"] = string.IsNullOrWhiteSpace(settings.ValidationLevel) ? "standard" : settings.ValidationLevel;
        mcp["processValidationEnabled"] = false;
        mcp["enabledToolOverrides"] = ToArray(settings.EnabledToolOverrides);
        mcp["disabledToolOverrides"] = ToArray(settings.DisabledToolOverrides);

        JsonObject policies = GetOrCreateObject(mcp, "connectionPolicies");
        JsonObject gateway = GetOrCreateObject(policies, "gateway");
        JsonObject direct = GetOrCreateObject(policies, "direct");
        gateway["allowed"] = true;
        gateway["requiresApproval"] = false;
        direct["allowed"] = true;
        direct["requiresApproval"] = false;
        policies["maxDirectConnections"] = settings.MaxDirectConnections;

        WriteRaw(raw);
    }

    JsonObject ReadRaw()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new JsonObject();

            JsonNode? node = JsonNode.Parse(File.ReadAllText(SettingsPath));
            return node as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    void WriteRaw(JsonObject raw)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath) ?? ".");
        string tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, raw.ToJsonString(m_WriteOptions));
        if (File.Exists(SettingsPath))
            File.Replace(tempPath, SettingsPath, null);
        else
            File.Move(tempPath, SettingsPath);
    }

    static JsonObject GetOrCreateObject(JsonObject owner, string name)
    {
        if (owner.TryGetPropertyValue(name, out JsonNode? node) && node is JsonObject obj)
            return obj;

        obj = new JsonObject();
        owner[name] = obj;
        return obj;
    }

    static bool GetBool(JsonObject owner, string name, bool fallback)
    {
        if (!owner.TryGetPropertyValue(name, out JsonNode? node) || node == null)
            return fallback;

        try { return node.GetValue<bool>(); }
        catch { return fallback; }
    }

    static int GetInt(JsonObject owner, string name, int fallback)
    {
        if (!owner.TryGetPropertyValue(name, out JsonNode? node) || node == null)
            return fallback;

        try { return node.GetValue<int>(); }
        catch { return fallback; }
    }

    static string GetString(JsonObject owner, string name, string fallback)
    {
        if (!owner.TryGetPropertyValue(name, out JsonNode? node) || node == null)
            return fallback;

        try { return node.GetValue<string>() ?? fallback; }
        catch { return fallback; }
    }

    static List<string> GetStringArray(JsonObject owner, string name)
    {
        if (!owner.TryGetPropertyValue(name, out JsonNode? node) || node is not JsonArray array)
            return [];

        return array
            .Select(item =>
            {
                try { return item?.GetValue<string>(); }
                catch { return null; }
            })
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static JsonArray ToArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (string value in values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            array.Add(value);
        }

        return array;
    }
}
