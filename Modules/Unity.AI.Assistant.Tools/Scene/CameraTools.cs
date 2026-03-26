using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;
using Unity.AI.Assistant.Editor;

namespace Unity.AI.Assistant.Tools.Editor
{
    class CameraTools
    {
        public const string ToolName = "Unity.Camera.Capture";
        const int k_RenderWidth = 1920;
        const int k_RenderHeight = 1080;

        [AgentTool(
            "Renders an image from a specific camera in the scene by GameObject instance ID that has a Camera component. " +
            "It is useful to capture a specific camera, in cases like camera placement or when this view doesn't match the scene/game view. " +
            "Optionally, it can be used without the camera ID parameter to capture the current scene view for cases when the scene view is not available on the screen. " +
            "Note: this is a computationally expensive tool, and should be used carefully only when scene view context is necessary.",
            ToolName,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag,
            Mcp = McpAvailability.Default)]
        internal static async Task<ImageOutput> CameraCaptureTool(
            ToolExecutionContext context,
            [Parameter("GameObject instance ID (e.g. 12345) that has a Camera component. If not provided, uses the Scene View camera.")]
            int? cameraInstanceID = null)
        {
            await context.Permissions.CheckScreenCapture();
            return CameraCaptureInternal(context, cameraInstanceID);
        }

        [ToolPermissionIgnore]  // To ignore delete warnings for temporary textures
        static ImageOutput CameraCaptureInternal(ToolExecutionContext context, int? cameraInstanceID)
        {
            Texture2D scenePreview = null;

            try
            {
                InternalLog.Log($"CameraCapture with cameraInstanceID: {cameraInstanceID}");

                Camera camera = null;
                string cameraDescription = "";

                // If a specific GameObject instance ID is provided, use it
                if (cameraInstanceID.HasValue)
                {
                    var cameraId = cameraInstanceID.Value;

#if UNITY_6000_3_OR_NEWER
                    GameObject targetGo = EditorUtility.EntityIdToObject(cameraId) as GameObject;
#else
                    GameObject targetGo = EditorUtility.InstanceIDToObject(cameraId) as GameObject;
#endif

                    if (targetGo == null)
                        throw new Exception($"No GameObject found with Instance ID {cameraId}.");

                    // Get the Camera component from the GameObject
                    camera = targetGo.GetComponentInChildren<Camera>();
                    if (camera == null)
                        throw new Exception($"GameObject '{targetGo.name}' (Instance ID: {cameraId}) does not have a Camera component. Cannot render image.");

                    cameraDescription = $"camera from GameObject '{targetGo.name}' (Instance ID: {cameraId})";
                    InternalLog.Log($"Using {cameraDescription}");
                }
                // Otherwise, use the Scene View camera
                else
                {
                    var sceneView = SceneView.lastActiveSceneView;
                    if (sceneView == null)
                        throw new Exception("Scene view is null. Please ensure a Scene View window is open.");

                    camera = sceneView.camera;
                    if (camera == null)
                        throw new Exception("Scene view camera is null.");

                    cameraDescription = "Scene View camera";
                    InternalLog.Log("Using Scene View camera");
                }

                scenePreview = camera.RenderToNewTexture(k_RenderWidth, k_RenderHeight);
                context.Permissions.IgnoreUnityObject(scenePreview);

                var imageOutput = new ImageOutput(scenePreview, $"Preview of the scene from {cameraDescription}", "Scene View");
                return imageOutput;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to render scene preview: {ex.Message}");
                throw new Exception("Failed to render scene preview.", ex);
            }
            finally
            {
                // Clean up temporary texture
                if (scenePreview != null)
                {
                    UnityEngine.Object.DestroyImmediate(scenePreview);
                }
            }
        }
    }
}

