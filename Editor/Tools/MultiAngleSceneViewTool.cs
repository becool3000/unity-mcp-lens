using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Tools.Editor
{
    class MultiAngleSceneViewTool
    {
        public const string ToolName = "Unity.SceneView.CaptureMultiAngleSceneView";

        // Multi-angle capture settings
        const int k_MultiAngleRenderWidth = 512;
        const int k_MultiAngleRenderHeight = 512;
        const float k_DefaultCameraDistance = 10f;
        const float k_BoundsMarginMultiplier = 2.0f;
        const int k_LabelFontSize = 24;
        const int k_LabelPadding = 10;

        /// <summary>
        /// Defines the 4 camera angles for multi-view screenshot capture.
        /// Each angle specifies: name, direction camera looks FROM, up vector for orientation, and whether to use perspective.
        /// </summary>
        static readonly (string Name, Vector3 Direction, Vector3 Up, bool UsePerspective)[] k_CameraAngles =
        {
            ("Iso", new Vector3(1f, 1f, 1f).normalized, Vector3.up, true),  // Isometric perspective view from elevated diagonal
            ("Front", Vector3.back, Vector3.up, false),                      // Orthographic front view
            ("Top", Vector3.up, Vector3.forward, false),                     // Orthographic top-down view
            ("Right", Vector3.right, Vector3.up, false)                      // Orthographic right side view
        };

        [AgentTool(
            "Captures a multi-angle view of the current Scene View from 4 different perspectives in a 2x2 grid (Isometric, Front, Top, Right). " +
            "This tool is designed for 3D projects and provides comprehensive visual context of 3D scene layout from multiple viewpoints, useful for validating 3D scene structure. " +
            "Not recommended for 2D projects. " +
            "The isometric view uses perspective projection for depth, while Front/Top/Right use orthographic projection. " +
            "Note: this is a computationally expensive tool, and should be used carefully only when multi-view context is necessary.",
            ToolName,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag,
            Mcp = McpAvailability.Default)]
        internal static async Task<ImageOutput> CaptureMultiAngleSceneView(ToolExecutionContext context)
        {
            await context.Permissions.CheckScreenCapture();

            GameObject tempCameraGO = null;
            var capturedTextures = new List<Texture2D>();

            try
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                    throw new Exception("No active Scene View found. Please open a Scene View window.");

                var sceneViewCamera = sceneView.camera;
                if (sceneViewCamera == null)
                    throw new Exception("Scene View camera is null.");

                // Calculate scene bounds center and appropriate camera distance
                var (boundsCenter, cameraDistance) = CalculateSceneBoundsAndDistance();

                // Create a temporary camera for rendering (no permission check for internal temporary objects)
                tempCameraGO = SetupTemporaryCameraGO(context, sceneViewCamera, cameraDistance);

                var tempCamera = tempCameraGO.GetComponent<Camera>();

                // Capture from each angle
                foreach (var (angleName, direction, upVector, usePerspective) in k_CameraAngles)
                {
                    var cameraPosition = boundsCenter + direction * cameraDistance;
                    tempCameraGO.transform.position = cameraPosition;
                    tempCameraGO.transform.LookAt(boundsCenter, upVector);

                    // Configure projection mode per view
                    tempCamera.orthographic = !usePerspective;
                    if (usePerspective)
                    {
                        tempCamera.fieldOfView = 60f;
                    }
                    else
                    {
                        tempCamera.orthographicSize = cameraDistance * 0.5f;
                    }

                    // Render the camera to texture
                    var texture = tempCamera.RenderToNewTexture(k_MultiAngleRenderWidth, k_MultiAngleRenderHeight);
                    context.Permissions.IgnoreUnityObject(texture);

                    // Add label overlay
                    AddLabelToTexture(texture, angleName);

                    capturedTextures.Add(texture);
                    InternalLog.Log($"Captured {angleName} view from position {cameraPosition}, distance {cameraDistance:F1}, perspective: {usePerspective}");
                }

                // Stitch the 4 images in a 2x2 grid
                var mergedTexture = StitchTextures2x2(capturedTextures);
                context.Permissions.IgnoreUnityObject(mergedTexture);

                const string info = "Multi-angle view of the Scene View in a 2x2 grid showing Isometric, Front, Top, and Right perspectives. Each view is labeled.";
                var result = new ImageOutput(mergedTexture, info, "SceneMultiAngle");

                Object.DestroyImmediate(mergedTexture);

                InternalLog.Log($"Multi-angle scene view captured successfully. Size: {result.Metadata.SizeInBytes} bytes");

                return result;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to capture multi-angle scene view: {ex.Message}");
                throw new Exception($"Failed to capture multi-angle scene view: {ex.Message}");
            }
            finally
            {
                if (tempCameraGO != null)
                {
                    await context.Permissions.CheckUnityObjectAccess(IToolPermissions.ItemOperation.Delete, typeof(GameObject), tempCameraGO);
                    Object.DestroyImmediate(tempCameraGO);
                }

                foreach (var tex in capturedTextures)
                {
                    if (tex != null)
                        Object.DestroyImmediate(tex);
                }
            }
        }

        /// <summary>
        /// Creates and configures a temporary camera GameObject for multi-angle screenshot capture.
        /// This method is not subject to permission checks as it creates only temporary internal objects.
        /// </summary>
        [ToolPermissionIgnore]
        static GameObject SetupTemporaryCameraGO(ToolExecutionContext context, Camera sourceCamera, float cameraDistance)
        {
            // Create temporary GameObject with HideAndDontSave flag
            var tempCameraGO = new GameObject("TempMultiAngleCamera");
            tempCameraGO.hideFlags = HideFlags.HideAndDontSave;
            context.Permissions.IgnoreUnityObject(tempCameraGO);

            // Add Camera component
            var tempCamera = tempCameraGO.AddComponent<Camera>();
            context.Permissions.IgnoreUnityObject(tempCamera);

            // Copy base settings from Scene View camera
            tempCamera.CopyFrom(sourceCamera);
            tempCamera.enabled = false;
            tempCamera.nearClipPlane = 0.01f;
            tempCamera.farClipPlane = cameraDistance * 4f;

            return tempCameraGO;
        }

        /// <summary>
        /// Calculates the combined bounds of all active renderers in the scene.
        /// Returns the bounds center and an appropriate camera distance.
        /// Falls back to scene view pivot/size if no renderers are found.
        /// </summary>
        static (Vector3 center, float distance) CalculateSceneBoundsAndDistance()
        {
            var sceneView = SceneView.lastActiveSceneView;
            var fallbackPivot = sceneView != null ? sceneView.pivot : Vector3.zero;

            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            if (renderers == null || renderers.Length == 0)
            {
                // Try to use Scene View size as fallback
                if (sceneView != null && sceneView.size > 0)
                    return (fallbackPivot, sceneView.size * k_BoundsMarginMultiplier);

                return (fallbackPivot, k_DefaultCameraDistance);
            }

            var bounds = new Bounds();
            var hasBounds = false;

            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
                return (fallbackPivot, k_DefaultCameraDistance);

            // Use the largest dimension of the bounds to determine camera distance
            var maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            var distance = Mathf.Max(maxExtent * k_BoundsMarginMultiplier, k_DefaultCameraDistance);

            return (bounds.center, distance);
        }

        /// <summary>
        /// Adds a text label to the top-left corner of the texture.
        /// </summary>
        static void AddLabelToTexture(Texture2D texture, string label)
        {
            // Create a simple label background and text
            var labelWidth = label.Length * (k_LabelFontSize / 2) + k_LabelPadding * 2;
            var labelHeight = k_LabelFontSize + k_LabelPadding;

            // Semi-transparent black background for label
            var bgColor = new Color(0f, 0f, 0f, 0.7f);
            var textColor = Color.white;

            // Get all pixels for efficient batch processing
            var allPixels = texture.GetPixels();
            var width = texture.width;
            var height = texture.height;
            var bgStartY = height - labelHeight;
            var clampedLabelWidth = Mathf.Min(labelWidth, width);

            // Draw background rectangle at top-left using batch pixel update
            for (int y = bgStartY; y < height; y++)
            {
                for (int x = 0; x < clampedLabelWidth; x++)
                {
                    var pixelIndex = y * width + x;
                    var existingColor = allPixels[pixelIndex];
                    allPixels[pixelIndex] = Color.Lerp(existingColor, bgColor, bgColor.a);
                }
            }

            texture.SetPixels(allPixels);

            // Draw simple text using pixel patterns for each character
            DrawSimpleText(texture, label, k_LabelPadding, height - k_LabelFontSize, textColor);

            texture.Apply();
        }

        /// <summary>
        /// Draws simple text on a texture using basic pixel font patterns.
        /// </summary>
        static void DrawSimpleText(Texture2D texture, string text, int startX, int startY, Color color)
        {
            var charWidth = k_LabelFontSize / 2;
            var charHeight = k_LabelFontSize - 4;
            var x = startX;

            foreach (var c in text.ToUpper())
            {
                var pattern = GetCharacterPattern(c);
                if (pattern != null)
                {
                    DrawCharacter(texture, pattern, x, startY, charWidth, charHeight, color);
                }
                x += charWidth + 2;
            }
        }

        /// <summary>
        /// Gets a simple 5x7 pixel pattern for a character.
        /// </summary>
        static bool[,] GetCharacterPattern(char c)
        {
            return c switch
            {
                'F' => new bool[,]
                {
                    { true, true, true, true, true },
                    { true, false, false, false, false },
                    { true, true, true, true, false },
                    { true, false, false, false, false },
                    { true, false, false, false, false },
                    { true, false, false, false, false },
                    { true, false, false, false, false }
                },
                'R' => new bool[,]
                {
                    { true, true, true, true, false },
                    { true, false, false, false, true },
                    { true, false, false, false, true },
                    { true, true, true, true, false },
                    { true, false, true, false, false },
                    { true, false, false, true, false },
                    { true, false, false, false, true }
                },
                'O' => new bool[,]
                {
                    { false, true, true, true, false },
                    { true, false, false, false, true },
                    { true, false, false, false, true },
                    { true, false, false, false, true },
                    { true, false, false, false, true },
                    { true, false, false, false, true },
                    { false, true, true, true, false }
                },
                'N' => new bool[,]
                {
                    { true, false, false, false, true },
                    { true, true, false, false, true },
                    { true, false, true, false, true },
                    { true, false, true, false, true },
                    { true, false, false, true, true },
                    { true, false, false, false, true },
                    { true, false, false, false, true }
                },
                'T' => new bool[,]
                {
                    { true, true, true, true, true },
                    { false, false, true, false, false },
                    { false, false, true, false, false },
                    { false, false, true, false, false },
                    { false, false, true, false, false },
                    { false, false, true, false, false },
                    { false, false, true, false, false }
                },
                'P' => new bool[,]
                {
                    { true, true, true, true, false },
                    { true, false, false, false, true },
                    { true, false, false, false, true },
                    { true, true, true, true, false },
                    { true, false, false, false, false },
                    { true, false, false, false, false },
                    { true, false, false, false, false }
                },
                'I' => new bool[,]
                {
                    { true, true, true, true, true },
                    { false, false, true, false, false },
                    { false, false, true, false, false },
                    { false, false, true, false, false },
                    { false, false, true, false, false },
                    { false, false, true, false, false },
                    { true, true, true, true, true }
                },
                'G' => new bool[,]
                {
                    { false, true, true, true, false },
                    { true, false, false, false, true },
                    { true, false, false, false, false },
                    { true, false, true, true, true },
                    { true, false, false, false, true },
                    { true, false, false, false, true },
                    { false, true, true, true, false }
                },
                'H' => new bool[,]
                {
                    { true, false, false, false, true },
                    { true, false, false, false, true },
                    { true, false, false, false, true },
                    { true, true, true, true, true },
                    { true, false, false, false, true },
                    { true, false, false, false, true },
                    { true, false, false, false, true }
                },
                'S' => new bool[,]
                {
                    { false, true, true, true, false },
                    { true, false, false, false, true },
                    { true, false, false, false, false },
                    { false, true, true, true, false },
                    { false, false, false, false, true },
                    { true, false, false, false, true },
                    { false, true, true, true, false }
                },
                _ => null
            };
        }

        /// <summary>
        /// Draws a character pattern onto the texture using batch pixel updates.
        /// </summary>
        static void DrawCharacter(Texture2D texture, bool[,] pattern, int startX, int startY, int width, int height, Color color)
        {
            var patternHeight = pattern.GetLength(0);
            var patternWidth = pattern.GetLength(1);
            var scaleX = (float)width / patternWidth;
            var scaleY = (float)height / patternHeight;

            var allPixels = texture.GetPixels();
            var texWidth = texture.width;
            var texHeight = texture.height;

            for (int py = 0; py < patternHeight; py++)
            {
                for (int px = 0; px < patternWidth; px++)
                {
                    if (!pattern[py, px])
                        continue;

                    var x0 = startX + (int)(px * scaleX);
                    var y0 = startY + (int)((patternHeight - 1 - py) * scaleY);
                    var x1 = startX + (int)((px + 1) * scaleX);
                    var y1 = startY + (int)((patternHeight - py) * scaleY);

                    for (int y = y0; y < y1 && y < texHeight; y++)
                    {
                        for (int x = x0; x < x1 && x < texWidth; x++)
                        {
                            if (x >= 0 && y >= 0)
                            {
                                var pixelIndex = y * texWidth + x;
                                allPixels[pixelIndex] = color;
                            }
                        }
                    }
                }
            }

            texture.SetPixels(allPixels);
        }

        /// <summary>
        /// Stitches 4 textures into a 2x2 grid using efficient batch pixel operations.
        /// Layout: [0][1]
        ///         [2][3]
        /// </summary>
        static Texture2D StitchTextures2x2(List<Texture2D> textures)
        {
            if (textures == null || textures.Count != 4)
                throw new ArgumentException("Exactly 4 textures required for 2x2 grid.");

            // Assuming all textures are the same size
            var cellWidth = textures[0].width;
            var cellHeight = textures[0].height;
            var totalWidth = cellWidth * 2;
            var totalHeight = cellHeight * 2;

            var merged = new Texture2D(totalWidth, totalHeight, TextureFormat.ARGB32, false);

            // Fill with dark gray background
            var bgColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            var mergedPixels = new Color[totalWidth * totalHeight];
            for (int i = 0; i < mergedPixels.Length; i++)
                mergedPixels[i] = bgColor;

            // Grid positions: [0]=top-left, [1]=top-right, [2]=bottom-left, [3]=bottom-right
            var positions = new (int x, int y)[]
            {
                (0, cellHeight),           // Top-left
                (cellWidth, cellHeight),   // Top-right
                (0, 0),                    // Bottom-left
                (cellWidth, 0)             // Bottom-right
            };

            // Copy each texture into the merged image using row-by-row batch copy
            for (int i = 0; i < 4; i++)
            {
                var tex = textures[i];
                var sourcePixels = tex.GetPixels();
                var (xOffset, yOffset) = positions[i];

                for (int y = 0; y < tex.height; y++)
                {
                    // Copy entire row at once
                    System.Array.Copy(
                        sourcePixels, y * cellWidth,
                        mergedPixels, (yOffset + y) * totalWidth + xOffset,
                        cellWidth);
                }
            }

            merged.SetPixels(mergedPixels);
            merged.Apply();
            return merged;
        }
    }
}
