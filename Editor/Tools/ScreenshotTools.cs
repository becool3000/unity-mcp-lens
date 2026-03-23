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
    class ScreenshotTools
    {
        public const string ToolName = "Unity.EditorWindow.CaptureScreenshot";

        [AgentTool(
            "Captures a screenshot of the current state of the Unity Editor interface (focused windows). " +
            "It can be used to collect important visual context information when needed, including the scene appearance. " +
            "Note: this is a computationally expensive tool, and should be used carefully only when visual context is necessary.",
            ToolName,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag,
            Mcp = McpAvailability.Default)]
        internal static async Task<ImageOutput> CaptureScreenshot(ToolExecutionContext context)
        {
            await context.Permissions.CheckScreenCapture();

            try
            {
                var screenContext = ScreenContextUtility.CaptureScreenContext(includeScreenshots: true, saveToFile: false);
                if (screenContext.Screenshot == null)
                {
                    throw new Exception("No screenshot could be captured. Make sure Unity Editor windows are open and focused.");
                }

                const string info = "This is the screenshot of the current state of the Unity Editor.";

                var result = new ImageOutput(screenContext.Screenshot, screenContext.ScreenshotWidth, screenContext.ScreenshotHeight, description: info, displayName: "Screenshot");
                InternalLog.Log($"Screenshot captured successfully. Size: {result.Metadata.SizeInBytes} bytes");

                return result;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to capture screenshot: {ex.Message}");
                throw new Exception($"Failed to capture screenshot: {ex.Message}");
            }
        }
    }
}
