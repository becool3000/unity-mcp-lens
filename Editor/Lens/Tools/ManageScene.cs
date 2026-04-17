using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Becool.UnityMcpLens.Editor.Helpers; // For Response class
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Tools.Parameters;

namespace Becool.UnityMcpLens.Editor.Tools
{
    /// <summary>
    /// Handles scene management operations like loading, saving, creating, and querying hierarchy.
    /// </summary>
    public static class ManageScene
    {
        /// <summary>
        /// Description of the Unity.ManageScene tool for MCP clients.
        /// Provides information about scene management operations including load, save, create, and hierarchy queries.
        /// </summary>
        public const string Title = "Manage Unity scenes";

        public const string Description = @"Manages Unity scenes (load, save, create, get hierarchy, etc.).

Args:
    Action: Operation (e.g., 'Load', 'Save', 'Create', 'GetHierarchy').
    Name: Scene name (no extension) for create/load/save.
    Path: Asset path for scene operations (default: ""Assets/"").
    BuildIndex: Build index for load/build settings actions.
    # Add other action-specific args as needed (e.g., for hierarchy depth)

Returns:
    Dictionary with results ('success', 'message', 'data').";
        /// <summary>
        /// Returns the output schema for this tool.
        /// </summary>
        /// <returns>The JSON schema object describing the tool's output structure.</returns>
        [McpOutputSchema("Unity.ManageScene")]
        public static object GetOutputSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    success = new { type = "boolean", description = "Whether the operation succeeded" },
                    message = new { type = "string", description = "Human-readable message about the operation" },
                    data = new
                    {
                        type = "object",
                        description = "Scene-specific operation data",
                        properties = new
                        {
                            name = new { type = "string", description = "Scene name" },
                            path = new { type = "string", description = "Scene path" },
                            buildIndex = new { type = "integer", description = "Build index of the scene" },
                            isLoaded = new { type = "boolean", description = "Whether the scene is loaded" },
                            isDirty = new { type = "boolean", description = "Whether the scene has unsaved changes" },
                            rootCount = new { type = "integer", description = "Number of root GameObjects in the scene" },
                            guid = new { type = "string", description = "Scene GUID" },
                            enabled = new { type = "boolean", description = "Whether the scene is enabled in build settings" },
                            hierarchy = new { type = "array", description = "Scene hierarchy (for GetHierarchy action)" }
                        }
                    }
                },
                required = new[] { "success", "message" }
            };
        }

        /// <summary>
        /// Main handler for scene management actions.
        /// </summary>
        /// <param name="parameters">The parameters specifying the scene action and related settings.</param>
        /// <returns>A response object containing success status, message, and optional data.</returns>
        [McpTool("Unity.ManageScene", Description, Title, Groups = new string[] { "core", "scene" }, EnabledByDefault = true)]
        public static object HandleCommand(ManageSceneParams parameters)
        {
            var @params = parameters;

            string name = @params.Name;
            string path = @params.Path; // Relative to Assets/
            int? buildIndex = @params.BuildIndex;

            // Ensure path is relative to Assets/, removing any leading "Assets/"
            string relativeDir = path ?? string.Empty;
            if (!string.IsNullOrEmpty(relativeDir))
            {
                relativeDir = relativeDir.Replace('\\', '/').Trim('/');
                if (relativeDir.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    relativeDir = relativeDir.Substring("Assets/".Length).TrimStart('/');
                }
            }

            // Apply default *after* sanitizing, using the original path variable for the check
            if (string.IsNullOrEmpty(path) && @params.Action == SceneAction.Create) // Check original path for emptiness
            {
                relativeDir = "Scenes"; // Default relative directory
            }

            string sceneFileName = string.IsNullOrEmpty(name) ? null : $"{name}.unity";
            // Construct full system path correctly: ProjectRoot/Assets/relativeDir/sceneFileName
            string fullPathDir = Path.Combine(Application.dataPath, relativeDir); // Combine with Assets path (Application.dataPath ends in Assets)
            string fullPath = string.IsNullOrEmpty(sceneFileName)
                ? null
                : Path.Combine(fullPathDir, sceneFileName);
            // Ensure relativePath always starts with "Assets/" and uses forward slashes
            string relativePath = string.IsNullOrEmpty(sceneFileName)
                ? null
                : Path.Combine("Assets", relativeDir, sceneFileName).Replace('\\', '/');

            // Ensure directory exists for 'create'
            if (@params.Action == SceneAction.Create && !string.IsNullOrEmpty(fullPathDir))
            {
                try
                {
                    Directory.CreateDirectory(fullPathDir);
                }
                catch (Exception e)
                {
                    return Response.Error(
                        $"Could not create directory '{fullPathDir}': {e.Message}"
                    );
                }
            }

            // Route action
            switch (@params.Action)
            {
                case SceneAction.Create:
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relativePath))
                        return Response.Error(
                            "'name' and 'path' parameters are required for 'create' action."
                        );
                    return CreateScene(fullPath, relativePath);
                case SceneAction.Load:
                    // Loading can be done by path/name or build index
                    if (!string.IsNullOrEmpty(relativePath))
                        return LoadScene(relativePath);
                    else if (buildIndex.HasValue)
                        return LoadScene(buildIndex.Value);
                    else
                        return Response.Error(
                            "Either 'name'/'path' or 'buildIndex' must be provided for 'load' action."
                        );
                case SceneAction.Save:
                    // Save current scene, optionally to a new path
                    return SaveScene(fullPath, relativePath);
                case SceneAction.GetHierarchy:
                    return GetSceneHierarchy(@params.Depth ?? -1);
                case SceneAction.GetActive:
                    return GetActiveSceneInfo();
                case SceneAction.GetBuildSettings:
                    return GetBuildSettingsScenes();
                case SceneAction.GetInfo:
                    return GetSceneInfo();
                case SceneAction.FindObjects:
                    return FindSceneObjects(@params.SearchTerm, @params.IncludeInactive, @params.Limit ?? 80);
                case SceneAction.GetVisibleObjects:
                    return GetVisibleObjects(@params.CameraName, @params.IncludeInactive, @params.Limit ?? 80);
                // Add cases for modifying build settings, additive loading, unloading etc.
                default:
                    return Response.Error(
                        $"Unknown action: '{@params.Action}'. Valid actions: Create, Load, Save, GetHierarchy, GetActive, GetBuildSettings, GetInfo, FindObjects, GetVisibleObjects."
                    );
            }
        }

        static object CreateScene(string fullPath, string relativePath)
        {
            if (File.Exists(fullPath))
            {
                return Response.Error($"Scene already exists at '{relativePath}'.");
            }

            try
            {
                // Create a new empty scene
                Scene newScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single
                );
                // Save it to the specified path
                bool saved = EditorSceneManager.SaveScene(newScene, relativePath);

                if (saved)
                {
                    AssetDatabase.Refresh(); // Ensure Unity sees the new scene file

                    return Response.Success(
                        $"Scene '{Path.GetFileName(relativePath)}' created successfully at '{relativePath}'.",
                        new { path = relativePath }
                    );
                }
                else
                {
                    // If SaveScene fails, it might leave an untitled scene open.
                    // Optionally try to close it, but be cautious.
                    return Response.Error($"Failed to save new scene to '{relativePath}'.");
                }
            }
            catch (Exception e)
            {
                return Response.Error($"Error creating scene '{relativePath}': {e.Message}");
            }
        }

        static object LoadScene(string relativePath)
        {
            if (
                !File.Exists(
                    Path.Combine(
                        Application.dataPath.Substring(
                            0,
                            Application.dataPath.Length - "Assets".Length
                        ),
                        relativePath
                    )
                )
            )
            {
                return Response.Error($"Scene file not found at '{relativePath}'.");
            }

            // Check for unsaved changes in the current scene
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                // Optionally prompt the user or save automatically before loading
                return Response.Error(
                    "Current scene has unsaved changes. Please save or discard changes before loading a new scene."
                );
                // Example: bool saveOK = EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                // if (!saveOK) return Response.Error("Load cancelled by user.");
            }

            try
            {
                EditorSceneManager.OpenScene(relativePath, OpenSceneMode.Single);

                return Response.Success(
                    $"Scene '{relativePath}' loaded successfully.",
                    new
                    {
                        path = relativePath,
                        name = Path.GetFileNameWithoutExtension(relativePath),
                    }
                );
            }
            catch (Exception e)
            {
                return Response.Error($"Error loading scene '{relativePath}': {e.Message}");
            }
        }

        static object LoadScene(int buildIndex)
        {
            if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                return Response.Error(
                    $"Invalid build index: {buildIndex}. Must be between 0 and {SceneManager.sceneCountInBuildSettings - 1}."
                );
            }

            // Check for unsaved changes
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                return Response.Error(
                    "Current scene has unsaved changes. Please save or discard changes before loading a new scene."
                );
            }

            try
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                return Response.Success(
                    $"Scene at build index {buildIndex} ('{scenePath}') loaded successfully.",
                    new
                    {
                        path = scenePath,
                        name = Path.GetFileNameWithoutExtension(scenePath),
                        buildIndex = buildIndex,
                    }
                );
            }
            catch (Exception e)
            {
                return Response.Error(
                    $"Error loading scene with build index {buildIndex}: {e.Message}"
                );
            }
        }

        static object SaveScene(string fullPath, string relativePath)
        {
            try
            {
                Scene currentScene = EditorSceneManager.GetActiveScene();
                if (!currentScene.IsValid())
                {
                    return Response.Error("No valid scene is currently active to save.");
                }

                bool saved;
                string finalPath = currentScene.path; // Path where it was last saved or will be saved

                if (!string.IsNullOrEmpty(relativePath) && currentScene.path != relativePath)
                {
                    // Save As...
                    // Ensure directory exists
                    string dir = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    saved = EditorSceneManager.SaveScene(currentScene, relativePath);
                    finalPath = relativePath;
                }
                else
                {
                    // Save (overwrite existing or save untitled)
                    if (string.IsNullOrEmpty(currentScene.path))
                    {
                        // Scene is untitled, needs a path
                        return Response.Error(
                            "Cannot save an untitled scene without providing a 'name' and 'path'. Use Save As functionality."
                        );
                    }
                    saved = EditorSceneManager.SaveScene(currentScene);
                }

                if (saved)
                {
                    AssetDatabase.Refresh();

                    return Response.Success(
                        $"Scene '{currentScene.name}' saved successfully to '{finalPath}'.",
                        new { path = finalPath, name = currentScene.name }
                    );
                }
                else
                {
                    return Response.Error($"Failed to save scene '{currentScene.name}'.");
                }
            }
            catch (Exception e)
            {
                return Response.Error($"Error saving scene: {e.Message}");
            }
        }

        static object GetActiveSceneInfo()
        {
            try
            {
                Scene activeScene = EditorSceneManager.GetActiveScene();
                if (!activeScene.IsValid())
                {
                    return Response.Error("No active scene found.");
                }

                var sceneInfo = new
                {
                    name = activeScene.name,
                    path = activeScene.path,
                    buildIndex = activeScene.buildIndex, // -1 if not in build settings
                    isDirty = activeScene.isDirty,
                    isLoaded = activeScene.isLoaded,
                    rootCount = activeScene.rootCount,
                };

                return Response.Success("Retrieved active scene information.", sceneInfo);
            }
            catch (Exception e)
            {
                return Response.Error($"Error getting active scene info: {e.Message}");
            }
        }

        static object GetBuildSettingsScenes()
        {
            try
            {
                var scenes = new List<object>();
                for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                {
                    var scene = EditorBuildSettings.scenes[i];
                    scenes.Add(
                        new
                        {
                            path = scene.path,
                            guid = scene.guid.ToString(),
                            enabled = scene.enabled,
                            buildIndex = i, // Actual build index considering only enabled scenes might differ
                        }
                    );
                }
                return Response.Success("Retrieved scenes from Build Settings.", scenes);
            }
            catch (Exception e)
            {
                return Response.Error($"Error getting scenes from Build Settings: {e.Message}");
            }
        }

        static object GetSceneInfo()
        {
            try
            {
                var scenes = new List<object>();
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    scenes.Add(new
                    {
                        scene.name,
                        scene.path,
                        scene.buildIndex,
                        scene.isLoaded,
                        scene.isDirty,
                        scene.rootCount
                    });
                }

                var active = EditorSceneManager.GetActiveScene();
                return Response.Success("Retrieved loaded scene information.", new
                {
                    activeScene = new { active.name, active.path, active.buildIndex, active.isLoaded, active.isDirty, active.rootCount },
                    loadedSceneCount = SceneManager.sceneCount,
                    scenes
                });
            }
            catch (Exception e)
            {
                return Response.Error($"Error getting scene info: {e.Message}");
            }
        }

        static object FindSceneObjects(string searchTerm, bool includeInactive, int limit)
        {
            try
            {
                string query = (searchTerm ?? string.Empty).Trim();
                int max = Math.Clamp(limit, 1, 500);
                var inactiveMode = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
                var objects = UnityEngine.Object.FindObjectsByType<GameObject>(inactiveMode, FindObjectsSortMode.None)
                    .Where(go => string.IsNullOrEmpty(query) ||
                        go.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        GetHierarchyPath(go.transform).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        string.Equals(go.tag, query, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(LayerMask.LayerToName(go.layer), query, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(go => go.scene.path, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(go => GetHierarchyPath(go.transform), StringComparer.OrdinalIgnoreCase)
                    .Take(max)
                    .Select(go => new
                    {
                        go.name,
                        path = GetHierarchyPath(go.transform),
                        scenePath = go.scene.path,
                        instanceID = go.GetInstanceID(),
                        go.tag,
                        layer = LayerMask.LayerToName(go.layer),
                        go.activeSelf,
                        go.activeInHierarchy,
                        componentCount = go.GetComponents<Component>().Count(component => component != null)
                    })
                    .ToArray();

                return Response.Success("Scene object search completed.", new
                {
                    searchTerm = query,
                    includeInactive,
                    returned = objects.Length,
                    truncated = objects.Length == max,
                    objects
                });
            }
            catch (Exception e)
            {
                return Response.Error($"Error finding scene objects: {e.Message}");
            }
        }

        static object GetVisibleObjects(string cameraName, bool includeInactive, int limit)
        {
            try
            {
                var camera = ResolveCamera(cameraName);
                if (camera == null)
                    return Response.Error("No camera found for visibility query.");

                int max = Math.Clamp(limit, 1, 500);
                var planes = GeometryUtility.CalculateFrustumPlanes(camera);
                var inactiveMode = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
                var objects = UnityEngine.Object.FindObjectsByType<Renderer>(inactiveMode, FindObjectsSortMode.None)
                    .Where(renderer => renderer != null && renderer.gameObject.scene.IsValid() && GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                    .Take(max)
                    .Select(renderer => new
                    {
                        renderer.gameObject.name,
                        path = GetHierarchyPath(renderer.transform),
                        scenePath = renderer.gameObject.scene.path,
                        instanceID = renderer.gameObject.GetInstanceID(),
                        rendererType = renderer.GetType().FullName,
                        bounds = new
                        {
                            center = new { x = renderer.bounds.center.x, y = renderer.bounds.center.y, z = renderer.bounds.center.z },
                            size = new { x = renderer.bounds.size.x, y = renderer.bounds.size.y, z = renderer.bounds.size.z }
                        }
                    })
                    .ToArray();

                return Response.Success("Visible object query completed.", new
                {
                    camera = camera.name,
                    returned = objects.Length,
                    truncated = objects.Length == max,
                    objects
                });
            }
            catch (Exception e)
            {
                return Response.Error($"Error getting visible objects: {e.Message}");
            }
        }

        static Camera ResolveCamera(string cameraName)
        {
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (!string.IsNullOrWhiteSpace(cameraName))
            {
                var named = cameras.FirstOrDefault(camera => string.Equals(camera.name, cameraName, StringComparison.OrdinalIgnoreCase));
                if (named != null)
                    return named;
            }

            return Camera.main ?? cameras.FirstOrDefault();
        }

        static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;

            var names = new Stack<string>();
            while (transform != null)
            {
                names.Push(transform.name);
                transform = transform.parent;
            }

            return string.Join("/", names);
        }

        static object GetSceneHierarchy(int depth = -1)
        {
            try
            {
                Scene activeScene = EditorSceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    return Response.Error(
                        "No valid and loaded scene is active to get hierarchy from."
                    );
                }

                GameObject[] rootObjects = activeScene.GetRootGameObjects();
                var hierarchy = rootObjects.Select(go => GetGameObjectDataRecursive(go, depth)).ToList();

                return Response.Success(
                    $"Retrieved hierarchy for scene '{activeScene.name}'.",
                    hierarchy
                );
            }
            catch (Exception e)
            {
                return Response.Error($"Error getting scene hierarchy: {e.Message}");
            }
        }

        /// <summary>
        /// Recursively builds a data representation of a GameObject and its children.
        /// </summary>
        static object GetGameObjectDataRecursive(GameObject go, int depth = -1)
        {
            if (go == null)
                return null;

            var childrenData = new List<object>();
            if (depth != 0) // < 0 take all hierarchy, > 0 reduce the depth until we reach 0
            {
                foreach (Transform child in go.transform)
                {
                    childrenData.Add(GetGameObjectDataRecursive(child.gameObject, depth - 1));
                }
            }

            var gameObjectData = new Dictionary<string, object>
            {
                { "name", go.name },
                { "activeSelf", go.activeSelf },
                { "activeInHierarchy", go.activeInHierarchy },
                { "tag", go.tag },
                { "layer", go.layer },
                { "isStatic", go.isStatic },
                { "instanceID", go.GetInstanceID() }, // Useful unique identifier
                // Remove transform information since large scenes can have a lot of objects and this will
                // be too much data
                // {
                //     "transform",
                //     new
                //     {
                //         position = new
                //         {
                //             x = go.transform.localPosition.x,
                //             y = go.transform.localPosition.y,
                //             z = go.transform.localPosition.z,
                //         },
                //         rotation = new
                //         {
                //             x = go.transform.localRotation.eulerAngles.x,
                //             y = go.transform.localRotation.eulerAngles.y,
                //             z = go.transform.localRotation.eulerAngles.z,
                //         }, // Euler for simplicity
                //         scale = new
                //         {
                //             x = go.transform.localScale.x,
                //             y = go.transform.localScale.y,
                //             z = go.transform.localScale.z,
                //         },
                //     }
                // },
                { "children", childrenData },
            };

            return gameObjectData;
        }
    }
}

