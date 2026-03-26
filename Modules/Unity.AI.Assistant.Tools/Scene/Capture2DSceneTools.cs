using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Tools.Editor
{
    /// <summary>
    /// Tool for capturing a specific world-space region of a 2D scene using orthographic projection.
    /// Ideal for 2D scenes, tilemaps, or capturing specific areas of a scene.
    /// </summary>
    class Capture2DSceneTools
    {
        public const string ToolName = "Unity.SceneView.Capture2DScene";

        [AgentTool(
            "Captures a specific rectangular region of a 2D scene using orthographic (top-down) projection. " +
            "Specify world coordinates (X, Y) and dimensions (width, height) to capture exactly that area. " +
            "Ideal for 2D scenes, tilemaps, or capturing specific portions of a scene. " +
            "The pixelsPerUnit parameter controls resolution (higher = more detail but larger image).",
            ToolName,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag,
            Mcp = McpAvailability.Default)]
        internal static async Task<ImageOutput> Capture2DScene(
            ToolExecutionContext context,
            [Parameter("X coordinate of the bottom-left corner in world space")]
            float worldX,
            [Parameter("Y coordinate of the bottom-left corner in world space")]
            float worldY,
            [Parameter("Width of the region to capture in world units")]
            float worldWidth,
            [Parameter("Height of the region to capture in world units")]
            float worldHeight,
            [Parameter("Resolution in pixels per world unit (default: 32). Higher values = more detail.")]
            int pixelsPerUnit = 32,
            [Parameter("Background color as hex string (default: transparent). Example: '#000000' for black, '#FFFFFF' for white.")]
            string backgroundColor = null)
        {
            await context.Permissions.CheckScreenCapture();

            if (worldWidth <= 0)
                throw new ArgumentException("worldWidth must be positive");
            if (worldHeight <= 0)
                throw new ArgumentException("worldHeight must be positive");
            if (pixelsPerUnit <= 0 || pixelsPerUnit > 256)
                throw new ArgumentException("pixelsPerUnit must be between 1 and 256");

            return Capture2DSceneInternal(context, worldX, worldY, worldWidth, worldHeight, pixelsPerUnit, backgroundColor);
        }

        [ToolPermissionIgnore]
        static ImageOutput Capture2DSceneInternal(
            ToolExecutionContext context,
            float worldX,
            float worldY,
            float worldWidth,
            float worldHeight,
            int pixelsPerUnit,
            string backgroundColor)
        {
            // Parse background color
            Color bgColor;
            if (string.IsNullOrEmpty(backgroundColor))
            {
                bgColor = new Color(0, 0, 0, 0); // Transparent
            }
            else if (!ColorUtility.TryParseHtmlString(backgroundColor, out bgColor))
            {
                bgColor = new Color(0, 0, 0, 0);
                InternalLog.LogWarning($"Invalid background color '{backgroundColor}', using transparent");
            }

            int textureWidth = Mathf.CeilToInt(worldWidth * pixelsPerUnit);
            int textureHeight = Mathf.CeilToInt(worldHeight * pixelsPerUnit);

            // Cap texture size to prevent memory issues
            const int maxDimension = 4096;
            if (textureWidth > maxDimension || textureHeight > maxDimension)
            {
                float scale = Mathf.Min((float)maxDimension / textureWidth, (float)maxDimension / textureHeight);
                textureWidth = Mathf.CeilToInt(textureWidth * scale);
                textureHeight = Mathf.CeilToInt(textureHeight * scale);
                InternalLog.LogWarning($"Texture size capped to {textureWidth}x{textureHeight} to prevent memory issues");
            }

            var rt = RenderTexture.GetTemporary(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32);
            GameObject tempGO = null;
            RenderTexture previousRT = RenderTexture.active;
            Texture2D texture = null;

            try
            {
                tempGO = new GameObject("__SceneRegionCaptureCamera")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                context.Permissions.IgnoreUnityObject(tempGO);

                var camera = tempGO.AddComponent<Camera>();
                context.Permissions.IgnoreUnityObject(camera);
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = worldHeight * 0.5f;
                camera.transform.position = new Vector3(
                    worldX + worldWidth * 0.5f,
                    worldY + worldHeight * 0.5f,
                    -1000f);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 2000f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = bgColor;
                camera.cullingMask = ~0; // Render all layers
                camera.targetTexture = rt;
                camera.aspect = (float)textureWidth / textureHeight;

                camera.Render();

                RenderTexture.active = rt;
                texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
                context.Permissions.IgnoreUnityObject(texture);
                texture.ReadPixels(new Rect(0, 0, textureWidth, textureHeight), 0, 0);
                texture.Apply();

                var description = $"Orthographic capture of scene region: " +
                    $"origin=({worldX}, {worldY}), size=({worldWidth}x{worldHeight} units), " +
                    $"resolution={textureWidth}x{textureHeight}px ({pixelsPerUnit} px/unit)";

                var result = new ImageOutput(texture, description, "2DSceneCapture");

                InternalLog.Log($"Scene region captured: {description}");

                return result;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to capture scene region: {ex.Message}");
                throw new Exception($"Failed to capture scene region: {ex.Message}");
            }
            finally
            {
                RenderTexture.active = previousRT;
                RenderTexture.ReleaseTemporary(rt);

                if (tempGO != null)
                    Object.DestroyImmediate(tempGO);

                if (texture != null)
                    Object.DestroyImmediate(texture);
            }
        }
    }
}
