using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Editor.Serialization;
using Unity.AI.Assistant.Utils;
using Unity.Serialization.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

[assembly: InternalsVisibleTo("Unity.AI.Assistant.Tests")]

namespace Unity.AI.Assistant.Editor
{
    static class UnityDataUtils
    {
        /// <summary>
        /// Describes the type of rendering pipeline to retrieve
        /// </summary>
        public enum RenderingPipeLineType
        {
            /// <summary>The default rendering pipeline for the project</summary>
            Default = 0,
            /// <summary>The active rendering pipeline for the current quality level</summary>
            Current = 1
        }

        static ListRequest s_ListRequest;
        static Dictionary<string, string> s_PackageMap = new();

        static int s_PackageUpdateCount = 0;

        static ISerializationOverrideProvider s_SerializationOverrideProvider;

        /// <summary>
        /// Queries the package manager for all packages in the project and caches the results
        /// </summary>
        /// <param name="invalidate">If ture, any pre-cached package list will be cleared</param>
        public static void CachePackageData(bool invalidate)
        {
            if (invalidate)
            {
                s_PackageMap.Clear();
            }

            EditorApplication.update -= CachePackagesUpdate;
            EditorApplication.update += CachePackagesUpdate;

            if (s_ListRequest != null && !s_ListRequest.IsCompleted)
                return;

            s_PackageUpdateCount++;
            s_ListRequest = UnityEditor.PackageManager.Client.List(false, true);
        }

        public static bool PackageDataReady()
        {
            // Process a completed request eagerly so callers polling this method
            // don't depend on EditorApplication.update pumping (e.g. during async tests).
            if (s_ListRequest != null && s_ListRequest.IsCompleted)
            {
                if (s_ListRequest.Status == UnityEditor.PackageManager.StatusCode.Success)
                    s_PackageMap = s_ListRequest.Result.ToDictionary(p => p.name, p => p.version);

                EditorApplication.update -= CachePackagesUpdate;
                s_ListRequest = null;
            }

            return (s_ListRequest == null && s_PackageMap.Count > 0);
        }

        public static int PackageUpdateCount()
        {
            return s_PackageUpdateCount;
        }

        static void CachePackagesUpdate()
        {
            if (s_ListRequest == null)
            {
                EditorApplication.update -= CachePackagesUpdate;
                return;
            }

            if (s_ListRequest.IsCompleted)
            {
                // Save all the package data
                s_PackageMap = s_ListRequest.Result.ToDictionary(p => p.name, p => p.version);

                EditorApplication.update -= CachePackagesUpdate;
                s_ListRequest = null;
            }
        }

        /// <summary>
        /// Returns the active render pipeline for the current quality level or the default rendering pipeline for the project.
        /// </summary>
        /// <param name="type"> Whether to return the default rendering pipeline.</param>
        /// <returns>The rendering pipeline of the project.</returns>
        internal static string GetProjectRenderingPipeline(RenderingPipeLineType type = RenderingPipeLineType.Current)
        {
            var renderingPipeline = type == RenderingPipeLineType.Default
                ? GraphicsSettings.defaultRenderPipeline
                : GraphicsSettings.currentRenderPipeline;
            if (renderingPipeline == null)
            {
                return "No rendering pipeline is currently selected, the built-in pipeline is used.";
            }
            return renderingPipeline.name;
        }

        /// <summary>
        /// Returns the target platform for the current build settings.
        /// </summary>
        /// <returns>The current build target platform.</returns>
        internal static string GetTargetPlatform()
        {
            return EditorUserBuildSettings.activeBuildTarget.ToString();
        }

        /// <summary>
        /// Return the current api compatibility level for the project.
        /// </summary>
        /// <returns> The current API compatibility. </returns>
        internal static string GetCompatibilityLevel()
        {
            var buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            NamedBuildTarget namedBuildTarget = default;

            if (buildTargetGroup != BuildTargetGroup.Standalone)
                namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
            else
                namedBuildTarget = EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server
                    ? NamedBuildTarget.Server
                    : NamedBuildTarget.Standalone;

            var apiCompatibilityLevel = PlayerSettings.GetApiCompatibilityLevel(namedBuildTarget);
            return apiCompatibilityLevel.ToString();
        }

        /// <summary>
        /// Return the hierarchy of the current scene.
        /// </summary>
        /// <returns> The hierarchy of the current scene. </returns>
        internal static string GetCurrentSceneHierarchy()
        {
            var hierarchy = new StringBuilder();
            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var obj in rootObjects)
            {
                hierarchy.Append(obj.name).Append("\n");

                GetChildGameObjects(obj, ref hierarchy, "  ");
            }

            return hierarchy.ToString();
        }

        static void GetChildGameObjects(GameObject obj, ref StringBuilder hierarchy, string indent)
        {
            foreach (Transform child in obj.transform)
            {
                hierarchy.Append(indent).Append(child.name).Append("\n");
                GetChildGameObjects(child.gameObject, ref hierarchy, indent + "  ");
            }
        }

        /// <summary>
        /// Return the hierarchy of the Assets folder.
        /// </summary>
        /// <returns> The hierarchy of the Assets folder. </returns>
        public static string GetProjectHierarchy(string path, string indent = "")
        {
            StringBuilder hierarchy = new StringBuilder();
            string[] directories = Directory.GetDirectories(path);
            foreach (string directory in directories)
            {
                hierarchy.AppendLine(indent + Path.GetFileName(directory) + "/");
                hierarchy.Append(GetProjectHierarchy(directory, indent + "  "));
            }

            string[] files = Directory.GetFiles(path);
            foreach (string file in files)
            {
                hierarchy.AppendLine(indent + Path.GetFileName(file));
            }

            return hierarchy.ToString();
        }

        /// <summary>
        /// Return the current input system for the project.
        /// </summary>
        /// <returns> The current input system</returns>
        internal static string GetInputSystem()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            return "New Input System";
#elif ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
            return "Both New Input System and Legacy Input Manager";
#elif ENABLE_LEGACY_INPUT_MANAGER && !ENABLE_INPUT_SYSTEM
            return "Legacy Input Manager";
#else
            return "None";
#endif
        }

        /// <summary>
        /// Returns a dictionary of package->version dependencies for the project
        /// </summary>
        /// <returns>A dictionary where the package is the key and version is the value</returns>
        public static Dictionary<string, string> GetPackageMap()
        {
            if (s_PackageMap.Count == 0)
                InternalLog.LogWarning("No package data available. Please call CachePackageData first.");

            return s_PackageMap;
        }

        /// <summary>
        /// Returns the Unity project root path.
        /// </summary>
        /// <returns>The project root directory path.</returns>
        internal static string GetProjectRootPath()
        {
            return Application.dataPath.Substring(0, Application.dataPath.LastIndexOf("/Assets", StringComparison.Ordinal));
        }

        /// <summary>
        /// Returns the Unity Assets folder path.
        /// </summary>
        /// <returns>The Assets directory path.</returns>
        internal static string GetAssetsPath()
        {
            return Application.dataPath;
        }

        /// <summary>
        /// Returns the Unity Packages folder path.
        /// </summary>
        /// <returns>The Packages directory path.</returns>
        internal static string GetPackagesPath()
        {
            var projectRoot = GetProjectRootPath();
            return Path.Combine(projectRoot, "Packages");
        }

        /// <summary>
        /// Returns the Unity ProjectSettings folder path.
        /// </summary>
        /// <returns>The ProjectSettings directory path.</returns>
        internal static string GetProjectSettingsPath()
        {
            var projectRoot = GetProjectRootPath();
            return Path.Combine(projectRoot, "ProjectSettings");
        }

        /// <summary>
        /// Returns the Unity Library folder path.
        /// </summary>
        /// <returns>The Library directory path.</returns>
        internal static string GetLibraryPath()
        {
            var projectRoot = GetProjectRootPath();
            return Path.Combine(projectRoot, "Library");
        }

        /// <summary>
        /// Returns the project configuration summary including rendering pipeline, target platform, api compatibility level, input system, and project paths.
        /// </summary>
        /// <returns>The project configuration summary.</returns>
        internal static Dictionary<string, string> GetProjectSettingSummary()
        {
            return new Dictionary<string, string>
            {
                { "Active Rendering Pipeline", GetProjectRenderingPipeline() },
                { "Target Platform/OS", GetTargetPlatform() },
                { "API Compatibility Level", GetCompatibilityLevel() },
                { "Input System", GetInputSystem() },
                { "Unity Version", ProjectVersionUtils.GetProjectVersion(ProjectVersionUtils.VersionDetail.Patch) },
                { "Project Root Path", GetProjectRootPath() },
                { "Assets Path", GetAssetsPath() },
                { "Packages Path", GetPackagesPath() },
                { "ProjectSettings Path", GetProjectSettingsPath() },
                { "Library Path", GetLibraryPath() }
            };
        }

        public static string GetProjectId()
        {
            return $"{AssistantConstants.ProjectIdTagPrefix}{PlayerSettings.productGUID.ToString().Replace("-", "")}";
        }

        /// <summary>
        /// Returns a string summary of the given log
        /// </summary>
        /// <param name="logData">The stored data for a single log entry</param>
        /// <param name="includeSource">If true, the content of the related source file will be included</param>
        /// <returns>A string summary of the given log message</returns>
        public static string OutputLogData(LogData logData, bool includeSource)
        {
            if (includeSource)
            {
                var fileName = logData.File;
                if (!string.IsNullOrEmpty(fileName) && fileName.EndsWith(".cs") && File.Exists(fileName))
                {
                    return $"{logData.Message}\n{fileName} contains the following code:\n{File.ReadAllText(fileName)}";
                }
            }

            return logData.Message;
        }

        /// <summary>
        /// Returns true if the given rootField exists as a serialized property on the given object
        /// </summary>
        /// <param name="targetObject">The object to display as a string</param>
        /// <param name="rootField">The field to check. Cannot be null.</param>
        /// <returns></returns>
        public static bool UnityObjectFieldExists(Object targetObject, string rootField)
        {
            if (string.IsNullOrEmpty(rootField))
                throw new ArgumentException($"{nameof(rootField)} cannot be null or empty");

            if (targetObject == null)
                return false;

            var targetSerializedObject = new SerializedObject(targetObject);

            var iter = targetSerializedObject.GetIterator();
            iter.Next(true);

            do
            {
                if (iter.name == rootField)
                    return true;
            } while (iter.Next(false));

            return false;
        }

        /// <summary>
        /// Returns a string summary of the given object
        /// </summary>
        /// <param name="targetObject">The object to display as a string</param>
        /// <param name="includeTypes">If true, the type of each variable will be included in the output</param>
        /// <param name="includeTooltips">If true, the tooltip associated with each variable (if available) will be included in the output</param>
        /// <param name="maxDepth">If 0 or greater, how many levels deep of nested objects to travel</param>
        /// <param name="rootFields">The list of fields to write. Null means all fields.</param>
        /// <param name="useDisplayName">Write field using their beautified display name.</param>
        /// <param name="ignorePrefabInstance">If true, prefab instances are ignored.</param>
        /// <returns>A string summary of the given object and its components</returns>
        public static string OutputUnityObject(Object targetObject, bool includeTypes, bool includeTooltips,
            int maxDepth = -1, string[] rootFields = default, bool useDisplayName = false,
            bool ignorePrefabInstance = true, bool outputDirectory = false, bool includeObjectName = true, bool includeInstanceID = true,
            int jsonLengthLimit = -1)
        {
            if (targetObject == null)
                return string.Empty;

            var targetSerializedObject = new SerializedObject(targetObject);

            var adapters = new List<IJsonAdapter>();
            var parameters = new JsonSerializationParameters
            {
                DisableSerializedReferences = true,
                UserDefinedAdapters = adapters,
                Minified = true
            };
            var jsonAdapter = new SerializationObjectJsonAdapter();
            jsonAdapter.OutputType = includeTypes;
            jsonAdapter.OutputNonObviousTypes = true;
            jsonAdapter.OutputTooltip = includeTooltips;
            jsonAdapter.MaxObjectDepth = maxDepth;
            jsonAdapter.RootParameters = rootFields;
            jsonAdapter.RootObject = targetSerializedObject;
            jsonAdapter.UseDisplayName = useDisplayName;
            jsonAdapter.IgnorePrefabInstance = ignorePrefabInstance;
            jsonAdapter.OutputDirectory = outputDirectory;
            jsonAdapter.OverrideProvider = GetSerializationOverrideProvider();

            adapters.Add(jsonAdapter);

            var objectName = string.Empty;
            if (includeObjectName)
            {
                objectName = jsonAdapter.GetObjectKey(targetSerializedObject, includeInstanceID);
                if (string.IsNullOrEmpty(objectName))
                {
                    objectName = targetObject.GetType().ToString();
                }
            }

            // Try to find the best depth to serialize the object.
            // If we get a SerializationException, the length set to SerializationObjectJsonAdapter.JsonOutputLimit
            // was exceeded. We then try to find the best depth to serialize the object by binary search.
            var json = string.Empty;
            SerializationObjectJsonAdapter.JsonOutputLimit = jsonLengthLimit;

            jsonAdapter.MaxPropertyDepth = -1; // No limit to start with.
            int min = 0, max = 0; // Min and Max values to try in binary search.
            do
            {
                try
                {
                    json = JsonSerialization.ToJson(targetSerializedObject, parameters);
                    // Serialization succeeded. If serialization failed previously, try to increase the depth.
                    if (jsonAdapter.MaxPropertyDepth != -1 && jsonAdapter.MaxPropertyDepth < max)
                    {
                        min = jsonAdapter.MaxPropertyDepth; // The min is the last successful depth.
                        SetNewDepth();
                    }
                    else
                    {
                        break;
                    }
                }
                catch (SerializationObjectJsonAdapter.SerializationException e)
                {
                    // Set max to the highest possible depth to try:
                    if (max == 0)
                    {
                        max = e.Depth - 1;
                    }
                    else
                    {
                        max = Math.Max(0, jsonAdapter.MaxPropertyDepth - 1);
                    }

                    SetNewDepth();
                }
            } while (jsonAdapter.MaxPropertyDepth > 0);

            return $"{objectName}\n{json}";

            void SetNewDepth()
            {
                // Set depth to midpoint between min and max to search our way to the highest possible value:
                var newDepth = (min + max) / 2;
                if (newDepth <= 0 || newDepth == jsonAdapter.MaxPropertyDepth)
                {
                    // If the new depth is the same as the old depth, we're close to the max, try that:
                    newDepth = max;
                }

                jsonAdapter.MaxPropertyDepth = newDepth;
            }
        }

        /// <summary>
        /// Retrieves a list of all project settings assets for serialization
        /// </summary>
        /// <returns>A list of tuples containing each project setting asset loaded and its project asset name</returns>
        public static List<Tuple<UnityEngine.Object, string>> GetSettingsAssets()
        {
            var settingsList = new List<Tuple<UnityEngine.Object, string>>();

            var assetsPath = Application.dataPath;
            var projectSettingsPath =
                assetsPath.Substring(0,
                    assetsPath.LastIndexOf("/Assets", StringComparison.Ordinal)) +
                "/ProjectSettings";
            var assetPaths = Directory.EnumerateFiles(projectSettingsPath, "*.asset").ToArray();

            if (!assetPaths.Any())
            {
                Debug.LogError("Project settings cannot be found.");
                return settingsList;
            }

            foreach (var assetPath in assetPaths)
            {
                var filename = Path.GetFileName(assetPath);
                var localPath = $"ProjectSettings/{filename}";

                var type = AssetDatabase.GetMainAssetTypeAtPath(localPath);
                var asset = AssetDatabase.LoadAssetAtPath(localPath, type);
                if (asset == null)
                    continue;

                settingsList.Add(new Tuple<Object, string>(asset, filename));
            }

            return settingsList;
        }

        public static ISerializationOverrideProvider GetSerializationOverrideProvider()
        {
            if (s_SerializationOverrideProvider is not null)
                return s_SerializationOverrideProvider;

            var overrides = SerializationOverrideUtility
                .GetOverrideMethodsFromAttribute()
                .Select(t => SerializationOverrideUtility.CreateOverride(t.declaringType, t.field, t.@override));

            s_SerializationOverrideProvider = new SerializationOverrideProvider(overrides);
            return s_SerializationOverrideProvider;
        }
    }
}
