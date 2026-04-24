#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Models.Project;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Adapters.Unity.Project
{
    sealed class UnityInputSystemProjectAdapter
    {
        const string InputSystemPackageName = "com.unity.inputsystem";
        const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";

        public ActiveInputHandlerState ReadActiveInputHandler()
        {
            if (TryReadActiveInputHandlerViaPlayerSettings(out var state))
                return state;

            if (TryReadActiveInputHandlerViaSerializedSettings(out state))
                return state;

            return new ActiveInputHandlerState
            {
                mode = "unknown",
                rawValue = -1,
                source = "unavailable"
            };
        }

        public ActiveInputHandlerState BuildRequestedInputHandler(string mode)
        {
            int rawValue = NormalizeInputHandlerMode(mode) switch
            {
                "legacy" => 0,
                "inputSystem" => 1,
                "both" => 2,
                _ => -1
            };

            return new ActiveInputHandlerState
            {
                mode = NormalizeInputHandlerMode(mode),
                rawValue = rawValue,
                source = "requested"
            };
        }

        public bool TrySetActiveInputHandler(ActiveInputHandlerState requested, out string error)
        {
            error = null;
            if (requested == null || requested.rawValue < 0)
            {
                error = "Requested input handler mode is invalid.";
                return false;
            }

            if (TrySetActiveInputHandlerViaPlayerSettings(requested.rawValue, out error))
                return true;

            return TrySetActiveInputHandlerViaSerializedSettings(requested.rawValue, out error);
        }

        public void SaveProjectSettings()
        {
            AssetDatabase.SaveAssets();
        }

        public void RequestScriptReload()
        {
            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
        }

        public object ReadScriptingDefineSignals()
        {
            string symbols = string.Empty;
            try
            {
#if UNITY_2021_2_OR_NEWER
                var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
                symbols = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
#else
                symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
#endif
            }
            catch
            {
            }

            var parts = (symbols ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToArray();

            return new
            {
                selectedBuildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup.ToString(),
                activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                containsEnableInputSystem = parts.Contains("ENABLE_INPUT_SYSTEM"),
                containsEnableLegacyInputManager = parts.Contains("ENABLE_LEGACY_INPUT_MANAGER"),
                symbols = parts
            };
        }

        public object ReadInputSystemPackage()
        {
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(candidate => string.Equals(candidate.name, InputSystemPackageName, StringComparison.OrdinalIgnoreCase));

            if (package == null)
            {
                return new
                {
                    installed = false,
                    name = InputSystemPackageName
                };
            }

            return new
            {
                installed = true,
                package.name,
                package.displayName,
                package.version,
                package.source,
                package.packageId,
                package.assetPath,
                isEmbedded = package.source == PackageSource.Embedded,
                isLocal = package.source == PackageSource.Local || package.source == PackageSource.Embedded
            };
        }

        public object ReadInputSystemAssemblyStatus()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var assembly = assemblies.FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "Unity.InputSystem", StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
            {
                return new
                {
                    loaded = false,
                    typeLoadOk = false,
                    inputSystemType = (string)null,
                    error = "Unity.InputSystem assembly is not loaded."
                };
            }

            try
            {
                var inputSystemType = assembly.GetType("UnityEngine.InputSystem.InputSystem", throwOnError: false);
                return new
                {
                    loaded = true,
                    assembly = assembly.GetName().Name,
                    version = assembly.GetName().Version?.ToString(),
                    location = SafeAssemblyLocation(assembly),
                    typeLoadOk = inputSystemType != null,
                    inputSystemType = inputSystemType?.FullName
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    loaded = true,
                    assembly = assembly.GetName().Name,
                    version = assembly.GetName().Version?.ToString(),
                    location = SafeAssemblyLocation(assembly),
                    typeLoadOk = false,
                    errorType = ex.GetType().FullName,
                    error = ex.Message
                };
            }
        }

        public object ReadInputDevices(int maxItems)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "Unity.InputSystem", StringComparison.OrdinalIgnoreCase));
            var inputSystemType = assembly?.GetType("UnityEngine.InputSystem.InputSystem", throwOnError: false);
            var devicesProperty = inputSystemType?.GetProperty("devices", BindingFlags.Public | BindingFlags.Static);
            if (devicesProperty == null)
            {
                return new
                {
                    available = false,
                    count = 0,
                    devices = Array.Empty<object>()
                };
            }

            try
            {
                var devicesValue = devicesProperty.GetValue(null) as IEnumerable;
                var devices = new List<object>();
                int total = 0;
                if (devicesValue != null)
                {
                    foreach (var device in devicesValue)
                    {
                        total++;
                        if (devices.Count >= maxItems)
                            continue;

                        devices.Add(new
                        {
                            name = ReadProperty(device, "name"),
                            displayName = ReadProperty(device, "displayName"),
                            path = ReadProperty(device, "path"),
                            layout = ReadProperty(device, "layout"),
                            enabled = ReadProperty(device, "enabled")
                        });
                    }
                }

                return new
                {
                    available = true,
                    count = total,
                    returned = devices.Count,
                    devices
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    available = false,
                    count = 0,
                    devices = Array.Empty<object>(),
                    errorType = ex.GetType().FullName,
                    error = ex.Message
                };
            }
        }

        public object ReadInputActionAssets(string requestedAssetPath, bool includeBindings, int maxItems)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var assetPaths = ResolveInputActionAssetPaths(projectRoot, requestedAssetPath, maxItems);
            var assets = assetPaths.Select(path => ReadInputActionAsset(projectRoot, path, includeBindings, maxItems)).ToArray();
            return new
            {
                count = assetPaths.Count,
                returned = assets.Length,
                assets
            };
        }

        public object ReadEditorLogSignals(int maxItems)
        {
            string path = ResolveEditorLogPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new
                {
                    path,
                    exists = false,
                    count = 0,
                    signals = Array.Empty<object>()
                };
            }

            string text = ReadFileTail(path, 1024 * 1024);
            var terms = new[] { "InputSystem", "Input System", ".inputactions", "TypeLoadException", "ENABLE_INPUT_SYSTEM", "activeInputHandling" };
            var signals = text.Replace("\r\n", "\n")
                .Split('\n')
                .Where(line => terms.Any(term => line.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                .Reverse()
                .Take(Math.Max(1, maxItems))
                .Reverse()
                .Select((line, index) => new
                {
                    index,
                    message = line.Trim()
                })
                .ToArray();

            return new
            {
                path,
                exists = true,
                count = signals.Length,
                signals
            };
        }

        static bool TryReadActiveInputHandlerViaPlayerSettings(out ActiveInputHandlerState state)
        {
            state = null;
            try
            {
                var property = typeof(PlayerSettings).GetProperty("activeInputHandling", BindingFlags.Public | BindingFlags.Static);
                if (property == null)
                    return false;

                var value = property.GetValue(null);
                int raw = Convert.ToInt32(value);
                state = new ActiveInputHandlerState
                {
                    mode = InputHandlerRawToMode(raw),
                    rawValue = raw,
                    source = $"PlayerSettings.activeInputHandling:{value}"
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool TryReadActiveInputHandlerViaSerializedSettings(out ActiveInputHandlerState state)
        {
            state = null;
            var playerSettings = LoadPlayerSettingsObject();
            if (playerSettings == null)
                return false;

            var serialized = new SerializedObject(playerSettings);
            var property = FindActiveInputHandlerProperty(serialized);
            if (property == null)
            {
                if (TryReadActiveInputHandlerFromProjectSettingsFile(out state))
                    return true;

                return false;
            }

            int raw = property.intValue;
            state = new ActiveInputHandlerState
            {
                mode = InputHandlerRawToMode(raw),
                rawValue = raw,
                source = "ProjectSettings.m_ActiveInputHandler"
            };
            return true;
        }

        static bool TrySetActiveInputHandlerViaPlayerSettings(int rawValue, out string error)
        {
            error = null;
            try
            {
                var property = typeof(PlayerSettings).GetProperty("activeInputHandling", BindingFlags.Public | BindingFlags.Static);
                if (property == null || !property.CanWrite)
                    return false;

                object value = Enum.ToObject(property.PropertyType, rawValue);
                property.SetValue(null, value);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static bool TrySetActiveInputHandlerViaSerializedSettings(int rawValue, out string error)
        {
            error = null;
            try
            {
                var playerSettings = LoadPlayerSettingsObject();
                if (playerSettings == null)
                {
                    error = "PlayerSettings object not found in ProjectSettings.asset.";
                    return false;
                }

                Undo.RecordObject(playerSettings, "Set Active Input Handler");
                var serialized = new SerializedObject(playerSettings);
                var property = FindActiveInputHandlerProperty(serialized);
                if (property == null)
                {
                    error = "activeInputHandler/m_ActiveInputHandler was not found in ProjectSettings.asset.";
                    return false;
                }

                property.intValue = rawValue;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(playerSettings);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static UnityEngine.Object LoadPlayerSettingsObject()
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsPath)
                .FirstOrDefault(candidate => candidate != null && candidate.GetType().Name == "PlayerSettings");
            if (asset != null)
                return asset;

            try
            {
                var method = typeof(Unsupported).GetMethod(
                    "GetSerializedAssetInterfaceSingleton",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null);

                return method?.Invoke(null, new object[] { "PlayerSettings" }) as UnityEngine.Object;
            }
            catch
            {
                return null;
            }
        }

        static SerializedProperty FindActiveInputHandlerProperty(SerializedObject serialized)
        {
            return serialized?.FindProperty("activeInputHandler") ??
                serialized?.FindProperty("m_ActiveInputHandler");
        }

        static bool TryReadActiveInputHandlerFromProjectSettingsFile(out ActiveInputHandlerState state)
        {
            state = null;
            try
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var settingsPath = Path.Combine(projectRoot, ProjectSettingsPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(settingsPath))
                    return false;

                foreach (var line in File.ReadLines(settingsPath))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("activeInputHandler:", StringComparison.Ordinal) &&
                        !trimmed.StartsWith("m_ActiveInputHandler:", StringComparison.Ordinal))
                        continue;

                    var valueText = trimmed.Split(':').LastOrDefault()?.Trim();
                    if (!int.TryParse(valueText, out int raw))
                        continue;

                    state = new ActiveInputHandlerState
                    {
                        mode = InputHandlerRawToMode(raw),
                        rawValue = raw,
                        source = "ProjectSettings.asset"
                    };
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        static string NormalizeInputHandlerMode(string mode)
        {
            string normalized = (mode ?? string.Empty).Trim();
            if (normalized.Equals("legacy", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("inputmanager", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("old", StringComparison.OrdinalIgnoreCase))
                return "legacy";

            if (normalized.Equals("inputSystem", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("input_system", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("new", StringComparison.OrdinalIgnoreCase))
                return "inputSystem";

            if (normalized.Equals("both", StringComparison.OrdinalIgnoreCase))
                return "both";

            return normalized;
        }

        static string InputHandlerRawToMode(int rawValue)
        {
            return rawValue switch
            {
                0 => "legacy",
                1 => "inputSystem",
                2 => "both",
                _ => "unknown"
            };
        }

        static string SafeAssemblyLocation(Assembly assembly)
        {
            try { return assembly.Location; }
            catch { return string.Empty; }
        }

        static object ReadProperty(object target, string propertyName)
        {
            if (target == null)
                return null;

            try
            {
                return target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        static List<string> ResolveInputActionAssetPaths(string projectRoot, string requestedAssetPath, int maxItems)
        {
            if (!string.IsNullOrWhiteSpace(requestedAssetPath))
                return new List<string> { NormalizeAssetPath(projectRoot, requestedAssetPath) };

            var assetsRoot = Path.Combine(projectRoot, "Assets");
            if (!Directory.Exists(assetsRoot))
                return new List<string>();

            return Directory.EnumerateFiles(assetsRoot, "*.inputactions", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
                .Take(Math.Max(1, maxItems))
                .ToList();
        }

        static string NormalizeAssetPath(string projectRoot, string assetPath)
        {
            var normalized = assetPath.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(normalized))
                return Path.GetRelativePath(projectRoot, normalized).Replace('\\', '/');

            return normalized;
        }

        static object ReadInputActionAsset(string projectRoot, string assetPath, bool includeBindings, int maxItems)
        {
            var fullPath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                return new
                {
                    path = assetPath,
                    exists = false
                };
            }

            try
            {
                var json = JObject.Parse(File.ReadAllText(fullPath));
                var maps = json["maps"] as JArray ?? new JArray();
                var bindings = maps.SelectMany(map => map["bindings"] as JArray ?? new JArray()).ToArray();
                var actions = maps.SelectMany(map => map["actions"] as JArray ?? new JArray()).ToArray();
                object[] bindingRows = includeBindings
                    ? bindings.Take(Math.Max(1, maxItems)).Select(binding => new
                    {
                        name = binding["name"]?.ToString(),
                        path = binding["path"]?.ToString(),
                        action = binding["action"]?.ToString(),
                        groups = binding["groups"]?.ToString(),
                        interactions = binding["interactions"]?.ToString(),
                        processors = binding["processors"]?.ToString(),
                        isComposite = binding["isComposite"]?.Value<bool?>(),
                        isPartOfComposite = binding["isPartOfComposite"]?.Value<bool?>()
                    }).Cast<object>().ToArray()
                    : Array.Empty<object>();

                var importer = AssetImporter.GetAtPath(assetPath);
                return new
                {
                    path = assetPath,
                    exists = true,
                    mapCount = maps.Count,
                    actionCount = actions.Length,
                    bindingCount = bindings.Length,
                    controlSchemeCount = (json["controlSchemes"] as JArray)?.Count ?? 0,
                    wrapperGeneration = ReadWrapperGeneration(importer),
                    bindings = bindingRows
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    path = assetPath,
                    exists = true,
                    parseOk = false,
                    errorType = ex.GetType().FullName,
                    error = ex.Message
                };
            }
        }

        static object ReadWrapperGeneration(AssetImporter importer)
        {
            if (importer == null)
                return new { available = false };

            var properties = importer.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.Name.IndexOf("wrapper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    property.Name.IndexOf("generate", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(property => new
                {
                    property = property.Name,
                    value = SafeReadProperty(importer, property)
                })
                .ToArray();

            return new
            {
                available = properties.Length > 0,
                importerType = importer.GetType().FullName,
                properties
            };
        }

        static object SafeReadProperty(object target, PropertyInfo property)
        {
            try { return property.GetValue(target); }
            catch { return null; }
        }

        static string ResolveEditorLogPath()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Logs", "Unity", "Editor.log");

            if (Application.platform == RuntimePlatform.WindowsEditor)
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".config", "unity3d", "Editor.log");
        }

        static string ReadFileTail(string path, int maxBytes)
        {
            var info = new FileInfo(path);
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > maxBytes)
                stream.Seek(-maxBytes, SeekOrigin.End);

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
