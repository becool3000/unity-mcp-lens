using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Tools.Editor
{
    class SettingsTools
    {
        const string k_GetProjectSettingsFunctionId = "Unity.GetProjectSettings";

        const int k_MaxArrayElements = 20;
        const int k_MaxDepth = 6;
        const int k_MaxCharacters = 16384;

        [Serializable]
        public class GetProjectSettingsOutput
        {
            public string Data = string.Empty;

            [Description("True if some properties were truncated because serialization depth was exceeded, false if the data is fully complete.")]
            public bool Truncated = false;
        }

        [AgentTool(
            "Extract the given Unity project settings.",
            k_GetProjectSettingsFunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag)]
        internal static async Task<GetProjectSettingsOutput> GetProjectSettings(
            ToolExecutionContext context,
            [Parameter("The name of the settings among: AudioManager, PhysicsManager, NavMeshProjectSettings, " +
                "MemorySettings, Physics2DSettings, EditorSettings, GraphicsSettings, ShaderGraphSettings, " +
                "UnityConnectSettings, VFXManager, XRSettings, PresetManager, TagManager, TimeManager, " +
                "VersionControlSettings, InputManager, PlayerSettings, QualitySettings, ShaderGraphSettings, MultiplayerManager.\n" +
                "Some settings availability can depend on the installed packages.")]
            string name,

            [Parameter("A specific property path to get data from, like 'm_Settings1/m_Settings2'. " +
                "Use brackets to indicate an index in an array property, like 'm_Settings1/m_Settings2[17]'. " +
                "Use this ONLY to get the value of truncated properties. Never guess the path. " +
                "Leave empty or null to get all the object properties instead.")]
            string propertyPath = null
        )
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Name cannot be null or empty.");

            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Read, ProjectSettingsPath);
            using var pooledSettingsAssets = ListPool<Object>.Get(out var settingsAssets);
            LoadAllProjectSettings(settingsAssets);

            Object foundSettings = null;
            foreach (var settingsAsset in settingsAssets)
            {
                // Note: settings that have a concrete C# name typically have an empty name
                var settingsName = settingsAsset.GetType() == typeof(Object) ?
                    settingsAsset.name :
                    settingsAsset.GetType().Name;

                if (settingsName == name)
                {
                    foundSettings = settingsAsset;
                    break;
                }
            }

            if (foundSettings == null)
                throw new ArgumentException($"The settings '{name}' was not found.");

            var serializedObject = new SerializedObject(foundSettings);
            var (json, serializationDepth) = serializedObject.ToJson(
                propertyPath: propertyPath,
                maxDepth: k_MaxDepth,
                maxArrayElements: k_MaxArrayElements,
                maxLength: k_MaxCharacters
            );

            var truncated = k_MaxDepth >= 0 && serializationDepth != k_MaxDepth;
            var formattedOutput = new GetProjectSettingsOutput
            {
                Data = json,
                Truncated = truncated
            };

            InternalLog.Log($"{formattedOutput.Data}\n\nTruncated: {formattedOutput.Truncated}");

            return formattedOutput;
        }

        static string ProjectSettingsPath => Path.Combine(Application.dataPath, "../ProjectSettings");

        static void LoadAllProjectSettings(List<Object> results)
        {
            var projectSettingsPath = ProjectSettingsPath;
            var filePaths = Directory.GetFiles(projectSettingsPath, "*.asset");
            foreach (var filePath in filePaths)
            {
                var filename = Path.GetFileName(filePath);
                var relativePath = $"ProjectSettings/{filename}";

                var type = AssetDatabase.GetMainAssetTypeAtPath(relativePath);
                if (type == null)
                    continue;

                var asset = AssetDatabase.LoadAssetAtPath(relativePath, type);
                if (asset == null)
                    continue;

                results.Add(asset);
            }
        }
    }
}
