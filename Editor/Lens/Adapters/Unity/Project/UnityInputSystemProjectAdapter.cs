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
            var package = FindRegisteredPackage(InputSystemPackageName);
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

        public ProjectPackageInfo ReadPackageInfo(string packageName)
        {
            string normalizedName = (packageName ?? string.Empty).Trim();
            string manifestVersion = ReadManifestDependencyVersion(normalizedName);
            var package = FindRegisteredPackage(normalizedName);

            return new ProjectPackageInfo
            {
                requestedName = normalizedName,
                installed = package != null,
                manifestVersion = manifestVersion,
                registeredVersion = package?.version,
                packageId = package?.packageId,
                assetPath = package?.assetPath,
                source = package != null ? package.source.ToString() : null,
                isEmbedded = package?.source == PackageSource.Embedded,
                isLocal = package != null && (package.source == PackageSource.Local || package.source == PackageSource.Embedded)
            };
        }

        public ProjectEditorInfo ReadEditorInfo()
        {
            return new ProjectEditorInfo
            {
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString()
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

        public InputActionAssetsSummary ReadInputActionAssets(string requestedAssetPath, bool includeBindings, int maxItems)
        {
            var projectRoot = GetProjectRoot();
            var assetPaths = ResolveInputActionAssetPaths(projectRoot, requestedAssetPath, maxItems);
            var assets = assetPaths
                .Select(path => InspectInputActionAsset(path, includeBindings, maxItems))
                .ToArray();
            return new InputActionAssetsSummary
            {
                count = assetPaths.Count,
                returned = assets.Length,
                assets = assets
            };
        }

        public InputActionsInspectResult InspectInputActionAsset(string assetPath, bool includeBindings, int maxItems)
        {
            string projectRoot = GetProjectRoot();
            string normalizedAssetPath = NormalizeAssetPath(projectRoot, assetPath);
            string fullPath = Path.Combine(projectRoot, normalizedAssetPath.Replace('/', Path.DirectorySeparatorChar));
            var importer = AssetImporter.GetAtPath(normalizedAssetPath);
            var wrapperGeneration = ReadWrapperGeneration(importer);
            var result = new InputActionsInspectResult
            {
                path = normalizedAssetPath,
                exists = File.Exists(fullPath),
                wrapperGeneration = wrapperGeneration
            };

            if (!result.exists)
            {
                result.issues.Add(CreateIssue("error", "asset_missing", $"Input actions asset '{normalizedAssetPath}' was not found."));
                return result;
            }

            try
            {
                var json = JObject.Parse(File.ReadAllText(fullPath));
                var mapEntries = (json["maps"] as JArray ?? new JArray())
                    .Select(map => new
                    {
                        mapName = map["name"]?.ToString(),
                        actions = map["actions"] as JArray ?? new JArray(),
                        bindings = map["bindings"] as JArray ?? new JArray()
                    })
                    .ToArray();
                var actions = mapEntries.SelectMany(map => map.actions).ToArray();
                var bindingEntries = mapEntries
                    .SelectMany(map => map.bindings.Select(binding => new InputActionBindingEntry
                    {
                        mapName = map.mapName,
                        binding = binding
                    }))
                    .ToArray();
                var bindings = bindingEntries.Select(entry => CreateBindingRow(entry.binding)).ToArray();

                result.mapCount = mapEntries.Length;
                result.actionCount = actions.Length;
                result.bindingCount = bindings.Length;
                result.controlSchemeCount = (json["controlSchemes"] as JArray)?.Count ?? 0;
                result.bindings = includeBindings
                    ? bindings.Take(Math.Max(1, maxItems)).ToArray()
                    : Array.Empty<InputActionBindingRow>();

                AddBindingIssues(result.issues, bindingEntries);
                foreach (var issue in wrapperGeneration.issues)
                    result.issues.Add(issue);

                return result;
            }
            catch (Exception ex)
            {
                result.issues.Add(CreateIssue("error", "parse_failed", $"Input actions asset '{normalizedAssetPath}' could not be parsed: {ex.Message}"));
                return result;
            }
        }

        public ProjectAssemblySignalsResult ReadPackageAssemblySignals(string packageName, int maxItems)
        {
            string normalizedName = (packageName ?? string.Empty).Trim();
            var package = FindRegisteredPackage(normalizedName);
            var allDescriptors = ResolvePackageAssemblyDescriptors(package);
            var descriptors = allDescriptors.Take(Math.Max(1, maxItems)).ToArray();

            return new ProjectAssemblySignalsResult
            {
                count = allDescriptors.Count,
                returned = descriptors.Length,
                assemblies = descriptors.Select(CreateAssemblySignal).ToArray()
            };
        }

        public ProjectEditorLogSignalsResult ReadEditorLogSignals(int maxItems)
        {
            return ReadEditorLogSignals(GetInputSystemLogTerms(null), maxItems);
        }

        public ProjectEditorLogSignalsResult ReadEditorLogSignals(IEnumerable<string> terms, int maxItems)
        {
            string path = ResolveEditorLogPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new ProjectEditorLogSignalsResult
                {
                    path = path,
                    exists = false,
                    count = 0,
                    signals = Array.Empty<ProjectEditorLogSignal>()
                };
            }

            var normalizedTerms = (terms ?? Array.Empty<string>())
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (normalizedTerms.Length == 0)
            {
                return new ProjectEditorLogSignalsResult
                {
                    path = path,
                    exists = true,
                    count = 0,
                    signals = Array.Empty<ProjectEditorLogSignal>()
                };
            }

            string text = ReadFileTail(path, 1024 * 1024);
            var signals = text.Replace("\r\n", "\n")
                .Split('\n')
                .Where(line => normalizedTerms.Any(term => line.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                .Reverse()
                .Take(Math.Max(1, maxItems))
                .Reverse()
                .Select((line, index) => new ProjectEditorLogSignal
                {
                    index = index,
                    message = line.Trim()
                })
                .ToArray();

            return new ProjectEditorLogSignalsResult
            {
                path = path,
                exists = true,
                count = signals.Length,
                signals = signals
            };
        }

        public IEnumerable<string> GetInputSystemLogTerms(string assetPath)
        {
            string normalizedAssetPath = string.IsNullOrWhiteSpace(assetPath) ? null : NormalizeAssetPath(GetProjectRoot(), assetPath);
            var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "InputSystem",
                "Input System",
                ".inputactions",
                "TypeLoadException",
                "ENABLE_INPUT_SYSTEM",
                "activeInputHandling",
                InputSystemPackageName,
                "Unity.InputSystem"
            };

            if (!string.IsNullOrWhiteSpace(normalizedAssetPath))
                terms.Add(Path.GetFileName(normalizedAssetPath));

            return terms;
        }

        public IEnumerable<string> GetPackageLogTerms(string packageName, ProjectAssemblySignalsResult assemblySignals, string assetPath = null)
        {
            var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                packageName,
                "TypeLoadException",
                "ReflectionTypeLoadException",
                "error CS",
                ".asmdef"
            };

            string packageLeaf = (packageName ?? string.Empty).Split('.').LastOrDefault();
            if (!string.IsNullOrWhiteSpace(packageLeaf))
                terms.Add(packageLeaf);

            foreach (var assembly in assemblySignals?.assemblies ?? Array.Empty<ProjectAssemblySignal>())
            {
                if (!string.IsNullOrWhiteSpace(assembly.name))
                    terms.Add(assembly.name);
            }

            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                terms.Add(".inputactions");
                terms.Add(Path.GetFileName(assetPath));
            }

            return terms;
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

        static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        static UnityEditor.PackageManager.PackageInfo FindRegisteredPackage(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return null;

            return UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(candidate => string.Equals(candidate.name, packageName, StringComparison.OrdinalIgnoreCase));
        }

        static string ReadManifestDependencyVersion(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return null;

            string manifestPath = Path.Combine(GetProjectRoot(), "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
                return null;

            try
            {
                var json = JObject.Parse(File.ReadAllText(manifestPath));
                var dependencies = json["dependencies"] as JObject;
                return dependencies?.Properties()
                    .FirstOrDefault(property => string.Equals(property.Name, packageName, StringComparison.OrdinalIgnoreCase))
                    ?.Value?.ToString();
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
            var normalized = (assetPath ?? string.Empty).Replace('\\', '/').Trim();
            if (Path.IsPathRooted(normalized))
                return Path.GetRelativePath(projectRoot, normalized).Replace('\\', '/');

            return normalized;
        }

        static InputActionBindingRow CreateBindingRow(JToken binding)
        {
            return new InputActionBindingRow
            {
                name = binding["name"]?.ToString(),
                path = binding["path"]?.ToString(),
                action = binding["action"]?.ToString(),
                groups = binding["groups"]?.ToString(),
                interactions = binding["interactions"]?.ToString(),
                processors = binding["processors"]?.ToString(),
                isComposite = binding["isComposite"]?.Value<bool?>(),
                isPartOfComposite = binding["isPartOfComposite"]?.Value<bool?>()
            };
        }

        sealed class InputActionBindingEntry
        {
            public string mapName { get; set; }
            public JToken binding { get; set; }
        }

        static void AddBindingIssues(List<ProjectDiagnosticIssue> issues, IEnumerable<InputActionBindingEntry> bindingEntries)
        {
            foreach (var entry in bindingEntries)
            {
                bool isComposite = entry.binding["isComposite"]?.Value<bool?>() ?? false;
                bool isPartOfComposite = entry.binding["isPartOfComposite"]?.Value<bool?>() ?? false;
                string path = entry.binding["path"]?.ToString();
                if (!isComposite && !isPartOfComposite && string.IsNullOrWhiteSpace(path))
                {
                    string action = entry.binding["action"]?.ToString() ?? "(unnamed)";
                    string mapName = entry.mapName ?? "(unnamed)";
                    issues.Add(CreateIssue("warning", "empty_binding_path", $"Binding '{action}' in map '{mapName}' has an empty control path."));
                }
            }

            var duplicateGroups = bindingEntries
                .Select(entry => new
                {
                    mapName = entry.mapName ?? string.Empty,
                    row = CreateBindingRow(entry.binding)
                })
                .GroupBy(entry => string.Join("|",
                    entry.mapName,
                    entry.row.action ?? string.Empty,
                    entry.row.name ?? string.Empty,
                    entry.row.path ?? string.Empty,
                    entry.row.groups ?? string.Empty,
                    entry.row.interactions ?? string.Empty,
                    entry.row.processors ?? string.Empty,
                    entry.row.isComposite?.ToString() ?? string.Empty,
                    entry.row.isPartOfComposite?.ToString() ?? string.Empty),
                    StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Take(8);

            foreach (var group in duplicateGroups)
            {
                var sample = group.First();
                issues.Add(CreateIssue(
                    "warning",
                    "duplicate_binding_tuple",
                    $"Binding '{sample.row.action ?? "(unnamed)"}' in map '{sample.mapName ?? "(unnamed)"}' has {group.Count()} duplicate binding tuples for path '{sample.row.path ?? "(empty)"}'."));
            }
        }

        static InputActionWrapperGeneration ReadWrapperGeneration(AssetImporter importer)
        {
            if (importer == null)
                return new InputActionWrapperGeneration { available = false };

            var importerType = importer.GetType();
            var serialized = new SerializedObject(importer);
            var generation = new InputActionWrapperGeneration
            {
                available = true,
                importerType = importerType.FullName,
                generateWrapperCode = SafeReadBoolProperty(importer, FindProperty(importerType, "generateWrapperCode")) ??
                    FindSerializedBoolProperty(serialized, "m_GenerateWrapperCode", "generateWrapperCode"),
                wrapperClassName = SafeReadStringProperty(importer, FindProperty(importerType, "wrapperClassName")) ??
                    FindSerializedStringProperty(serialized, "m_WrapperClassName", "wrapperClassName"),
                wrapperCodePath = SafeReadStringProperty(importer, FindProperty(importerType, "wrapperCodePath")) ??
                    FindSerializedStringProperty(serialized, "m_WrapperCodePath", "wrapperCodePath")
            };

            if (generation.generateWrapperCode == true && string.IsNullOrWhiteSpace(generation.wrapperClassName))
            {
                generation.issues.Add(CreateIssue("warning", "wrapper_class_missing", "Wrapper generation is enabled but wrapperClassName is empty."));
            }

            if (generation.generateWrapperCode == true && string.IsNullOrWhiteSpace(generation.wrapperCodePath))
            {
                generation.issues.Add(CreateIssue("warning", "wrapper_code_path_missing", "Wrapper generation is enabled but wrapperCodePath is empty."));
            }

            return generation;
        }

        static PropertyInfo FindProperty(Type targetType, string propertyName)
        {
            return targetType?
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        }

        static bool? SafeReadBoolProperty(object target, PropertyInfo property)
        {
            var value = SafeReadProperty(target, property);
            if (value == null)
                return null;

            if (value is bool boolValue)
                return boolValue;

            return bool.TryParse(value.ToString(), out boolValue) ? boolValue : null;
        }

        static string SafeReadStringProperty(object target, PropertyInfo property)
        {
            return SafeReadProperty(target, property)?.ToString();
        }

        static object SafeReadProperty(object target, PropertyInfo property)
        {
            try { return property?.GetValue(target); }
            catch { return null; }
        }

        static SerializedProperty FindSerializedProperty(SerializedObject serializedObject, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(propertyName))
                    continue;

                var property = serializedObject?.FindProperty(propertyName);
                if (property != null)
                    return property;
            }

            return null;
        }

        static bool? FindSerializedBoolProperty(SerializedObject serializedObject, params string[] propertyNames)
        {
            var property = FindSerializedProperty(serializedObject, propertyNames);
            return property?.propertyType == SerializedPropertyType.Boolean
                ? property.boolValue
                : null;
        }

        static string FindSerializedStringProperty(SerializedObject serializedObject, params string[] propertyNames)
        {
            var property = FindSerializedProperty(serializedObject, propertyNames);
            return property?.propertyType == SerializedPropertyType.String
                ? property.stringValue
                : null;
        }

        static ProjectDiagnosticIssue CreateIssue(string severity, string code, string message)
        {
            return new ProjectDiagnosticIssue
            {
                severity = severity,
                code = code,
                message = message
            };
        }

        sealed class PackageAssemblyDescriptor
        {
            public string Name { get; set; }
            public string SourcePath { get; set; }
        }

        static List<PackageAssemblyDescriptor> ResolvePackageAssemblyDescriptors(UnityEditor.PackageManager.PackageInfo package)
        {
            if (package == null)
                return new List<PackageAssemblyDescriptor>();

            string packageRoot = ResolvePackageRootPath(package.assetPath);
            if (string.IsNullOrWhiteSpace(packageRoot) || !Directory.Exists(packageRoot))
                return new List<PackageAssemblyDescriptor>();

            return Directory.EnumerateFiles(packageRoot, "*.asmdef", SearchOption.AllDirectories)
                .Where(path => !IsIgnoredAssemblyPath(path))
                .Select(path => TryReadAssemblyDescriptor(packageRoot, path))
                .Where(descriptor => descriptor != null)
                .GroupBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static string ResolvePackageRootPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            return Path.IsPathRooted(assetPath)
                ? assetPath
                : Path.GetFullPath(Path.Combine(GetProjectRoot(), assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        static bool IsIgnoredAssemblyPath(string path)
        {
            string normalized = path.Replace('\\', '/');
            return normalized.Contains("/Tests/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/Tests~/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/Samples~/", StringComparison.OrdinalIgnoreCase);
        }

        static PackageAssemblyDescriptor TryReadAssemblyDescriptor(string packageRoot, string path)
        {
            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                string name = json["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    return null;

                return new PackageAssemblyDescriptor
                {
                    Name = name,
                    SourcePath = Path.GetRelativePath(packageRoot, path).Replace('\\', '/')
                };
            }
            catch
            {
                return null;
            }
        }

        static ProjectAssemblySignal CreateAssemblySignal(PackageAssemblyDescriptor descriptor)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, descriptor.Name, StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
            {
                return new ProjectAssemblySignal
                {
                    name = descriptor.Name,
                    loaded = false,
                    typeLoadOk = false,
                    sourcePath = descriptor.SourcePath,
                    error = "Assembly is not loaded in the current AppDomain."
                };
            }

            try
            {
                _ = assembly.GetTypes();
                return new ProjectAssemblySignal
                {
                    name = descriptor.Name,
                    loaded = true,
                    typeLoadOk = true,
                    version = assembly.GetName().Version?.ToString(),
                    location = SafeAssemblyLocation(assembly),
                    sourcePath = descriptor.SourcePath
                };
            }
            catch (ReflectionTypeLoadException ex)
            {
                return new ProjectAssemblySignal
                {
                    name = descriptor.Name,
                    loaded = true,
                    typeLoadOk = false,
                    version = assembly.GetName().Version?.ToString(),
                    location = SafeAssemblyLocation(assembly),
                    sourcePath = descriptor.SourcePath,
                    errorType = ex.GetType().FullName,
                    error = ex.Message,
                    loaderErrorCount = ex.LoaderExceptions?.Count(loaderException => loaderException != null) ?? 0
                };
            }
            catch (Exception ex)
            {
                return new ProjectAssemblySignal
                {
                    name = descriptor.Name,
                    loaded = true,
                    typeLoadOk = false,
                    version = assembly.GetName().Version?.ToString(),
                    location = SafeAssemblyLocation(assembly),
                    sourcePath = descriptor.SourcePath,
                    errorType = ex.GetType().FullName,
                    error = ex.Message
                };
            }
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
