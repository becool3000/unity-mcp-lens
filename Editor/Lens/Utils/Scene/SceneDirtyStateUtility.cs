#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Utils.Scene
{
    sealed class SceneDirtyStateRow
    {
        public string name { get; set; }
        public string path { get; set; }
        public int buildIndex { get; set; }
        public int handle { get; set; }
        public bool isLoaded { get; set; }
        public bool isDirty { get; set; }
        public int rootCount { get; set; }
    }

    static class SceneDirtyStateUtility
    {
        public static object CaptureLoadedScenes()
        {
            var scenes = EnumerateLoadedScenes()
                .Select(ToSceneState)
                .ToArray();
            var active = EditorSceneManager.GetActiveScene();
            var dirtyScenePaths = scenes
                .Where(scene => scene.isDirty)
                .Select(scene => string.IsNullOrWhiteSpace(scene.path) ? scene.name : scene.path)
                .ToArray();

            return new
            {
                activeScene = active.IsValid() ? ToSceneState(active) : null,
                loadedSceneCount = scenes.Length,
                dirtySceneCount = dirtyScenePaths.Length,
                hasDirtyScenes = dirtyScenePaths.Length > 0,
                dirtyScenePaths,
                scenes
            };
        }

        public static object BuildSaveState(
            bool requested = false,
            bool attempted = false,
            bool saved = false,
            object savedScenes = null,
            string message = null,
            string error = null)
        {
            return new
            {
                requested,
                attempted,
                saved,
                savedScenes = savedScenes ?? Array.Empty<object>(),
                message = message ?? (requested ? "save_requested" : "not_requested"),
                error
            };
        }

        public static void MarkSceneDirty(GameObject gameObject)
        {
            if (gameObject != null && gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        public static bool TryResolveScene(string sceneTarget, out Scene scene, out string error)
        {
            error = null;
            scene = default;

            if (string.IsNullOrWhiteSpace(sceneTarget))
            {
                scene = EditorSceneManager.GetActiveScene();
                if (scene.IsValid())
                    return true;

                error = "No valid active scene is available.";
                return false;
            }

            string normalized = NormalizeScenePath(sceneTarget);
            foreach (Scene loadedScene in EnumerateLoadedScenes())
            {
                if (string.Equals(loadedScene.path, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(loadedScene.name, sceneTarget, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(loadedScene.path, sceneTarget, StringComparison.OrdinalIgnoreCase))
                {
                    scene = loadedScene;
                    return true;
                }
            }

            error = $"Loaded scene '{sceneTarget}' could not be found by name or path.";
            return false;
        }

        public static string NormalizeScenePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string normalized = path.Trim().Replace('\\', '/');
            if (normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    ? normalized
                    : "Assets/" + normalized.TrimStart('/');
            }

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? normalized : normalized + ".unity";

            if (normalized.Contains("/"))
                return "Assets/" + normalized.TrimStart('/') + ".unity";

            return normalized;
        }

        public static SceneDirtyStateRow ToSceneState(Scene scene)
        {
            return new SceneDirtyStateRow
            {
                name = scene.name,
                path = scene.path,
                buildIndex = scene.buildIndex,
                handle = scene.handle,
                isLoaded = scene.isLoaded,
                isDirty = scene.isDirty,
                rootCount = scene.rootCount
            };
        }

        public static IReadOnlyList<Scene> GetDirtyLoadedScenes()
        {
            return EnumerateLoadedScenes()
                .Where(scene => scene.isDirty)
                .ToArray();
        }

        static IEnumerable<Scene> EnumerateLoadedScenes()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded)
                    yield return scene;
            }
        }
    }
}
