#nullable disable
using System;
using System.Linq;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils.Scene;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    static class LensTransactionSnapshot
    {
        const int MaxDirtyAssets = 50;

        public static object Capture(string workflowId = null)
        {
            var connectionId = McpToolExecutionScope.Current?.ConnectionId;
            string[] activeToolPacks = string.IsNullOrWhiteSpace(connectionId)
                ? ToolPackCatalog.DefaultActivePacks
                : BridgeLensSessionRegistry.GetActiveToolPacks(connectionId);
            BridgeLensConnectionState connectionState = null;
            if (!string.IsNullOrWhiteSpace(connectionId))
                BridgeLensSessionRegistry.TryGetConnectionState(connectionId, out connectionState);

            var activeScene = EditorSceneManager.GetActiveScene();
            ConsoleCursorSnapshot console = ConsoleCursorDelta.Capture();

            return new
            {
                workflowId,
                capturedUtc = DateTime.UtcNow.ToString("O"),
                playMode = new
                {
                    isPlaying = EditorApplication.isPlaying,
                    isPaused = EditorApplication.isPaused,
                    isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode
                },
                activeScene = activeScene.IsValid()
                    ? new
                    {
                        name = activeScene.name,
                        path = activeScene.path,
                        isLoaded = activeScene.isLoaded,
                        isDirty = activeScene.isDirty,
                        buildIndex = activeScene.buildIndex
                    }
                    : null,
                compileImportState = new
                {
                    isCompiling = EditorApplication.isCompiling,
                    isUpdating = EditorApplication.isUpdating,
                    isBuildingPlayer = BuildPipeline.isBuildingPlayer
                },
                dirtyScenes = SceneDirtyStateUtility.CaptureLoadedScenes(),
                dirtyAssets = CaptureDirtyAssets(),
                consoleCursor = new
                {
                    cursor = console.Cursor,
                    errorCount = console.ErrorCount,
                    warningCount = console.WarningCount,
                    available = console.Available,
                    error = console.Error
                },
                activeToolPacks,
                selectedBridgeSessionId = connectionState?.LastKnownBridgeSessionId,
                selectedConnectionId = connectionId,
                manifestVersion = connectionState?.LastKnownManifestVersion
            };
        }

        static object CaptureDirtyAssets()
        {
            try
            {
                var paths = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                    .Where(obj => obj != null && EditorUtility.IsPersistent(obj) && EditorUtility.IsDirty(obj))
                    .Select(AssetDatabase.GetAssetPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Take(MaxDirtyAssets + 1)
                    .ToArray();

                return new
                {
                    available = true,
                    dirtyAssetCount = Math.Min(paths.Length, MaxDirtyAssets),
                    truncated = paths.Length > MaxDirtyAssets,
                    dirtyAssetPaths = paths.Take(MaxDirtyAssets).ToArray()
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    available = false,
                    error = ex.Message,
                    dirtyAssetCount = 0,
                    truncated = false,
                    dirtyAssetPaths = Array.Empty<string>()
                };
            }
        }
    }
}
