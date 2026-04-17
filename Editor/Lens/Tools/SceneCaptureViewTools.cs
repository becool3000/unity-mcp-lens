using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public record SceneCaptureViewParams
    {
        [McpDescription("Capture mode: scene_view, camera, game_view, or multi_angle.", Required = false, Default = "scene_view")]
        public string Mode { get; set; } = "scene_view";

        [McpDescription("Optional output path relative to the Unity project root, or unity://path/Assets/... URI. Defaults to Temp/LensCaptures.", Required = false)]
        public string OutputPath { get; set; }

        [McpDescription("Optional camera name for camera/game_view capture.", Required = false)]
        public string CameraName { get; set; }

        [McpDescription("Capture width in pixels.", Required = false, Default = 1280)]
        public int Width { get; set; } = 1280;

        [McpDescription("Capture height in pixels.", Required = false, Default = 720)]
        public int Height { get; set; } = 720;
    }

    public static class SceneCaptureViewTools
    {
        const string Description = "Captures scene, camera, game-view, or compact multi-angle images to disk and returns metadata only.";

        [McpTool("Unity.Scene.CaptureView", Description, "Capture Unity Scene View", Groups = new[] { "scene", "debug" }, EnabledByDefault = true)]
        public static object Capture(SceneCaptureViewParams parameters)
        {
            parameters ??= new SceneCaptureViewParams();
            string mode = (parameters.Mode ?? "scene_view").Trim().ToLowerInvariant();
            int width = Math.Clamp(parameters.Width, 64, 4096);
            int height = Math.Clamp(parameters.Height, 64, 4096);

            try
            {
                var captures = mode == "multi_angle"
                    ? CaptureMultiAngle(parameters, width, height)
                    : new[] { CaptureSingle(mode, parameters, width, height) };

                return Response.Success("Scene view capture completed.", new
                {
                    mode,
                    width,
                    height,
                    count = captures.Length,
                    captures
                });
            }
            catch (Exception ex)
            {
                return Response.Error($"SCENE_CAPTURE_FAILED: {ex.Message}");
            }
        }

        static object CaptureSingle(string mode, SceneCaptureViewParams parameters, int width, int height)
        {
            Camera camera = ResolveCamera(mode, parameters.CameraName);
            if (camera == null)
                throw new InvalidOperationException("No suitable camera was found for capture.");

            string outputPath = ResolveOutputPath(parameters.OutputPath, mode);
            CaptureCamera(camera, outputPath, width, height);
            return BuildCaptureMetadata(outputPath, mode, camera.name);
        }

        static object[] CaptureMultiAngle(SceneCaptureViewParams parameters, int width, int height)
        {
            Bounds bounds = ComputeSceneBounds();
            var tempObject = new GameObject("Lens Multi-Angle Capture Camera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var camera = tempObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 10000f;
            camera.fieldOfView = 45f;

            var center = bounds.center;
            float radius = Math.Max(1f, bounds.extents.magnitude);
            float distance = radius * 2.4f;

            var angles = new Dictionary<string, Vector3>
            {
                ["front"] = center + new Vector3(0f, radius * 0.35f, -distance),
                ["right"] = center + new Vector3(distance, radius * 0.35f, 0f),
                ["top"] = center + new Vector3(0f, distance, 0.01f),
                ["iso"] = center + new Vector3(distance, distance * 0.75f, -distance)
            };

            try
            {
                var captures = new List<object>();
                foreach (var kvp in angles)
                {
                    camera.transform.position = kvp.Value;
                    camera.transform.LookAt(center);
                    string output = ResolveOutputPath(parameters.OutputPath, $"multi_angle_{kvp.Key}");
                    CaptureCamera(camera, output, width, height);
                    captures.Add(BuildCaptureMetadata(output, kvp.Key, camera.name));
                }

                return captures.ToArray();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tempObject);
            }
        }

        static Camera ResolveCamera(string mode, string cameraName)
        {
            if (!string.IsNullOrWhiteSpace(cameraName))
            {
                var named = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(camera => string.Equals(camera.name, cameraName, StringComparison.OrdinalIgnoreCase));
                if (named != null)
                    return named;
            }

            if (mode == "scene_view")
                return SceneView.lastActiveSceneView?.camera ?? Camera.main;

            return Camera.main ?? UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault();
        }

        static void CaptureCamera(Camera camera, string outputPath, int width, int height)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var previousTarget = camera.targetTexture;
            var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);

            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply(updateMipmaps: false);
                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = null;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        static Bounds ComputeSceneBounds()
        {
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one * 5f);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        static string ResolveOutputPath(string requested, string suffix)
        {
            string projectRoot = ResourceUriHelper.ResolveProjectRoot(null);
            string normalizedSuffix = string.IsNullOrWhiteSpace(suffix) ? "capture" : suffix.Replace(' ', '_').ToLowerInvariant();
            string relative = string.IsNullOrWhiteSpace(requested)
                ? $"Temp/LensCaptures/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{normalizedSuffix}.png"
                : requested.Replace('\\', '/');

            if (relative.StartsWith("unity://path/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ResourceMutationTools.ResolveSafePath(relative, projectRoot);
                if (resolved == null)
                    throw new InvalidOperationException("OutputPath could not be resolved under the project root.");

                return resolved;
            }

            if (Path.IsPathRooted(relative))
                throw new InvalidOperationException("OutputPath must be relative to the Unity project root.");

            if (!relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                relative = $"{relative.TrimEnd('/')}/{normalizedSuffix}.png";

            var fullPath = Path.GetFullPath(Path.Combine(projectRoot, relative));
            if (!ResourceUriHelper.IsPathUnderProject(fullPath, projectRoot))
                throw new InvalidOperationException("OutputPath must stay under the Unity project root.");

            return fullPath;
        }

        static object BuildCaptureMetadata(string outputPath, string label, string cameraName)
        {
            string projectRoot = ResourceUriHelper.ResolveProjectRoot(null);
            var info = new FileInfo(outputPath);
            string relative = ResourceMutationTools.ToProjectRelativePath(projectRoot, outputPath);
            return new
            {
                label,
                cameraName,
                path = relative,
                uri = $"unity://path/{relative}",
                bytes = info.Exists ? info.Length : 0
            };
        }
    }
}
