using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITY_6000_3_OR_NEWER
using UnityEntityId = UnityEngine.EntityId;
#else
using UnityEntityId = System.Int32;
#endif

namespace Unity.AI.Generators.UI.Utilities
{
    static class ExternalFileDragDropConstants
    {
        public const string handlerType = "ai_toolkit_drag_and_drop_handler_type";
        public const string dropFileName = "ai_toolkit_drag_and_drop_file_name";
        public const string tempAssetPath = "ai_toolkit_drag_and_drop_temporary_asset_path";
        public const string externalFilePath = "ai_toolkit_drag_and_drop_external_file_path";
        public const string cacheHit = "ai_toolkit_drag_and_drop_cache_hit";
        public const string moveDepsFun = "ai_toolkit_drag_and_drop_move_deps_fun";
    }

    record CompareFunctionData(string sourcePath, string cachedAssetPath);
    record CopyFunctionData(string sourcePath, string destinationPath);
    record MoveFunctionData(string sourcePath, string destinationPath);
    static class ExternalFileDragDrop
    {
        static readonly string k_TemporaryDirectory = Path.Combine("Assets", "AI Toolkit", "Temp");
        static readonly string k_TemporaryDragAndDropDirectory = Path.Combine("Assets", "AI Toolkit", "Temp", "DragAndDrop");

        static readonly Queue<DragAndDropVisualMode> k_PreviousVisualModes = new();
        static int s_DragOverFrameCount;

        public static bool tempAssetDragged { get; private set; }

        [InitializeOnLoadMethod]
        static void Init()
        {
#if UNITY_6000_3_OR_NEWER
            DragAndDrop.AddDropHandlerV2(HandleDropHierarchy);
            DragAndDrop.AddDropHandlerV2(HandleDropProjectBrowser);
#else
            DragAndDrop.AddDropHandler(HandleDropHierarchy);
            DragAndDrop.AddDropHandler(HandleDropProjectBrowser);
#endif
            SceneView.duringSceneGui += HandleDropOnSceneView;
            DragAndDropCache.instance.EnsureSaved();
        }

        static void EnsureDirectoriesExist()
        {
            var parentDirectory = Path.Combine("Assets", "AI Toolkit");
            if (!AssetDatabase.IsValidFolder(parentDirectory))
                AssetDatabase.CreateFolder("Assets", "AI Toolkit");
            if (!AssetDatabase.IsValidFolder(k_TemporaryDirectory))
                AssetDatabase.CreateFolder("Assets/AI Toolkit", "Temp");
            if (!AssetDatabase.IsValidFolder(k_TemporaryDragAndDropDirectory))
                AssetDatabase.CreateFolder("Assets/AI Toolkit/Temp", "DragAndDrop");
        }

        static void StopTracking()
        {
            EditorApplication.update -= OnTrackingUpdate;
            tempAssetDragged = false;
        }

        static void StartTracking()
        {
            tempAssetDragged = true;
            EditorApplication.update += OnTrackingUpdate;

            s_DragOverFrameCount = 0;
            k_PreviousVisualModes.Clear();
            k_PreviousVisualModes.Enqueue(DragAndDrop.visualMode);
        }

        static bool HasTemporaryAssetInDrag()
        {
            if (DragAndDrop.GetGenericData(ExternalFileDragDropConstants.handlerType) as string != nameof(ExternalFileDragDrop))
                return false;
            return !string.IsNullOrEmpty(DragAndDrop.GetGenericData(ExternalFileDragDropConstants.tempAssetPath) as string);
        }

        static void OnTrackingUpdate()
        {
            k_PreviousVisualModes.Enqueue(DragAndDrop.visualMode);
            while (k_PreviousVisualModes.Count > 10)
                k_PreviousVisualModes.Dequeue();

            // When a UI element accepts the drag, and the mouse is released, objects are dropped and
            // cleared from DragAndDrop.objectReferences.
            // OnTrackingUpdate() will always know about the end of a drag operation after a RegisterValueChangedCallback
            // for an element that accepts the drag.
            // If the drag is canceled, OnTrackingUpdate() will also know about it
            // by monitoring the previous frame DragAndDrop visual mode.
            var hasTemporaryAssetInDrag = HasTemporaryAssetInDrag();
            if (hasTemporaryAssetInDrag && DragAndDrop.objectReferences is { Length: > 0 })
            {
                s_DragOverFrameCount = 0;
                return;
            }

            // Note that having no more objectReferences doesn't mean the drag is over.
            // There's a mysterious thing in imGUI when you drag items from a window to another where
            // the objectReferences are cleared for 2 frames but the drag is still ongoing.
            // We need to wait for 3 frames to be sure the drag is over.
            s_DragOverFrameCount++;
            if (s_DragOverFrameCount < 3)
                return;

            StopTracking();

            if (!hasTemporaryAssetInDrag)
            {
                ClearGenericData();
                return;
            }

            var assetPath = DragAndDrop.GetGenericData(ExternalFileDragDropConstants.tempAssetPath) as string;
            if (string.IsNullOrEmpty(assetPath))
            {
                ClearGenericData();
                return;
            }

            // Note that we don't have a good way of differentiating between accepting/canceling on valid targets (such as ESC or alt+tab)
            var accepted = k_PreviousVisualModes.Any(mode => mode is not (DragAndDropVisualMode.None or DragAndDropVisualMode.Rejected));
            if (accepted)
            {
                OnProjectWindowDragPerform(null, MoveFunction());
                ClearGenericData();
                return;
            }

            ClearGenericData();
            return;

            Func<MoveFunctionData, string> MoveFunction() => DragAndDrop.GetGenericData(ExternalFileDragDropConstants.moveDepsFun) as Func<MoveFunctionData, string>;
        }

        // Creates a temporary asset at drag start; if dropped in Project, it's moved at drop time.
        public static void StartDragFromExternalPath(string externalFilePath, string dropFileName = "",
            Func<CopyFunctionData, string> copyFunction = null,
            Func<MoveFunctionData, string> moveDependencies = null,
            Func<CompareFunctionData, bool> compareFunction = null)
        {
            if (!File.Exists(externalFilePath))
            {
                Debug.LogError("External file path not found: " + externalFilePath);
                return;
            }

            // if a dropFileName was provided without extension, use the extension of the external file
            if (!string.IsNullOrEmpty(dropFileName) && string.IsNullOrEmpty(Path.GetExtension(dropFileName)))
            {
                var extension = Path.GetExtension(externalFilePath);
                dropFileName = Path.ChangeExtension(dropFileName, extension);
            }

            var createdAsset = CreateTemporaryAssetInProject(externalFilePath, dropFileName, out var cacheHit, copyFunction, compareFunction);
            if (!createdAsset)
                return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.paths = new[] { AssetDatabase.GetAssetPath(createdAsset) };
            DragAndDrop.objectReferences = new[] { createdAsset };

            // Store the temporary asset path and optional dropFileName for final location
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.handlerType, nameof(ExternalFileDragDrop));
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.tempAssetPath, AssetDatabase.GetAssetPath(createdAsset));
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.dropFileName, dropFileName);
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.externalFilePath, externalFilePath);
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.cacheHit, cacheHit);
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.moveDepsFun, moveDependencies);

            DragAndDrop.StartDrag("Promote Generation to Project");

            StartTracking();
        }

        static void HandleDropOnSceneView(SceneView sceneView)
        {
            if (!HasTemporaryAssetInDrag())
                return;

            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragPerform:
                    try
                    {
                        StopTracking();
                        DragAndDrop.AcceptDrag();
                        OnProjectWindowDragPerform(null, MoveFunction());
                    }
                    finally
                    {
                        ClearGenericData();
                    }
                    break;
                case EventType.DragUpdated when DragAndDrop.objectReferences.Length != 0:
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    // do not call evt.Use(), we want the builtin Unity Editor Drag Updated to occur
                    break;
            }

            return;

            Func<MoveFunctionData, string> MoveFunction() => DragAndDrop.GetGenericData(ExternalFileDragDropConstants.moveDepsFun) as Func<MoveFunctionData, string>;
        }

        static DragAndDropVisualMode HandleDropProjectBrowser(UnityEntityId dragInstanceId, string dropUponPath, bool perform)
        {
            if (!HasTemporaryAssetInDrag())
                return DragAndDrop.visualMode;

            if (perform)
            {
                try
                {
                    StopTracking();
                    DragAndDrop.AcceptDrag();
                    OnProjectWindowDragPerform(dropUponPath, MoveFunction());
                }
                finally
                {
                    ClearGenericData();
                }
            }

            return DragAndDropVisualMode.Copy;

            Func<MoveFunctionData, string> MoveFunction() => DragAndDrop.GetGenericData(ExternalFileDragDropConstants.moveDepsFun) as Func<MoveFunctionData, string>;
        }

        static DragAndDropVisualMode HandleDropHierarchy(UnityEntityId dropTargetInstanceID, HierarchyDropFlags dropMode, Transform parentForDraggedObjects, bool perform)
        {
            if (!HasTemporaryAssetInDrag() || !perform)
                return DragAndDrop.visualMode;

            try
            {
                StopTracking();
                DragAndDrop.AcceptDrag();
                OnProjectWindowDragPerform(null, MoveFunction());
            }
            finally
            {
                ClearGenericData();
            }

            return DragAndDrop.visualMode;

            Func<MoveFunctionData, string> MoveFunction() => DragAndDrop.GetGenericData(ExternalFileDragDropConstants.moveDepsFun) as Func<MoveFunctionData, string>;
        }

        /// <summary>
        /// Move the temporary asset to its final location in the Project
        /// </summary>
        /// <param name="dropTargetPath"></param>
        /// <param name="moveDependenciesFunction"></param>
        static void OnProjectWindowDragPerform(string dropTargetPath, Func<MoveFunctionData, string> moveDependenciesFunction)
        {
            var tempPath = DragAndDrop.GetGenericData(ExternalFileDragDropConstants.tempAssetPath) as string;
            if (string.IsNullOrEmpty(tempPath))
                return;

            var fileName = Path.GetFileName(tempPath);
            var extension = Path.GetExtension(tempPath);

            var dropFileName = DragAndDrop.GetGenericData(ExternalFileDragDropConstants.dropFileName) as string;
            if (!string.IsNullOrEmpty(dropFileName))
                fileName = Path.ChangeExtension(dropFileName, extension);

            if (string.IsNullOrEmpty(dropTargetPath))
                dropTargetPath = "Assets";

            string newPath;
            if (AssetDatabase.IsValidFolder(dropTargetPath))
                newPath = Path.Combine(dropTargetPath, fileName);
            else
            {
                var folderPath = Path.GetDirectoryName(dropTargetPath);
                newPath = Path.Combine(folderPath ?? "Assets", fileName);
            }

            newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);
            // If the asset was already used before, copy it instead of moving it
            var cacheHit = (bool)DragAndDrop.GetGenericData(ExternalFileDragDropConstants.cacheHit);
            if (cacheHit)
                AssetDatabase.CopyAsset(tempPath, newPath);
            else
            {
                if (moveDependenciesFunction != null)
                    newPath = moveDependenciesFunction(new (tempPath, newPath));
                AssetDatabase.MoveAsset(tempPath, newPath);
            }

            var asset = AssetDatabase.LoadAssetAtPath<Object>(newPath);
            if (!asset)
                Debug.LogError($"Failed to load newly created asset at '{newPath}'");
            asset.EnableGenerationLabel();

            // Update the cache so that the new asset is the cached version.
            var externalFilePath = DragAndDrop.GetGenericData(ExternalFileDragDropConstants.externalFilePath) as string;
            if (string.IsNullOrEmpty(externalFilePath))
                return;
            var assetGuid = AssetDatabase.AssetPathToGUID(newPath);
            var currentDirectory = Directory.GetCurrentDirectory();
            var relativeExternalPath = Path.GetRelativePath(currentDirectory, externalFilePath);
            DragAndDropCache.instance.entries[relativeExternalPath] = assetGuid;
            DragAndDropCache.instance.EnsureSaved();
        }

        static void ClearGenericData(bool deleteTempAsset = true)
        {
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.handlerType, null);
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.tempAssetPath, null);
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.dropFileName, null);
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.externalFilePath, null);
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.cacheHit, null);
            DragAndDrop.SetGenericData(ExternalFileDragDropConstants.moveDepsFun, null);

            if (!deleteTempAsset)
                return;

            CleanupDragFromExternalPath();
        }

        public static void EndDragFromExternalPath()
        {
            if (!tempAssetDragged)
                return;

            StopTracking();
            ClearGenericData(false);
        }

        public static void CleanupDragFromExternalPath()
        {
            AssetDatabase.DeleteAsset(k_TemporaryDragAndDropDirectory);
            if (!Directory.EnumerateFileSystemEntries(k_TemporaryDirectory).Any())
                AssetDatabase.DeleteAsset(k_TemporaryDirectory);
        }

        static Object CreateTemporaryAssetInProject(string externalPath, string newFileName, out bool cacheHit,
            Func<CopyFunctionData, string> copyFunction = null,
            Func<CompareFunctionData, bool> compareFunction = null)
        {
            EnsureDirectoriesExist();

            cacheHit = false;
            var currentDirectory = Directory.GetCurrentDirectory();
            var relativeExternalPath = Path.GetRelativePath(currentDirectory, externalPath);
            if (DragAndDropCache.instance.entries.TryGetValue(relativeExternalPath, out var cachedGuid))
            {
                var cachedPath = AssetDatabase.GUIDToAssetPath(cachedGuid);
                if (!string.IsNullOrEmpty(cachedPath))
                {
                    var identical = compareFunction != null
                        ? compareFunction(new(externalPath, cachedPath))
                        : FileComparison.AreFilesIdentical(cachedPath, externalPath);
                    if (identical)
                    {
                        var cachedAsset = AssetDatabase.LoadAssetAtPath<Object>(cachedPath);
                        if (cachedAsset != null)
                        {
                            cacheHit = true;
                            return cachedAsset;
                        }
                    }
                }
                DragAndDropCache.instance.entries.Remove(relativeExternalPath);
            }

            newFileName = Path.GetFileName(!string.IsNullOrEmpty(newFileName) ? newFileName : externalPath);
            var newPath = Path.Combine(k_TemporaryDragAndDropDirectory, newFileName);
            newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);

            if (copyFunction != null)
                newPath = copyFunction(new (externalPath, newPath));
            else
                File.Copy(externalPath, newPath);
            AssetDatabase.ImportAsset(newPath, ImportAssetOptions.ForceUpdate);

            var asset = AssetDatabase.LoadAssetAtPath<Object>(newPath);
            if (!asset)
            {
                Debug.LogError($"Failed to load newly created asset at '{newPath}'");
                return null;
            }

            asset.EnableGenerationLabel();
            return asset;
        }
    }
}
