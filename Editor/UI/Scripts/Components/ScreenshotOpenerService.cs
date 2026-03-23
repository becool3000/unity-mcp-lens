using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Service that opens screenshots in the EditScreenCaptureWindow.
    /// This implementation lives in the UI assembly to avoid circular dependencies.
    /// </summary>
    internal class ScreenshotOpenerService : IScreenshotOpener
    {
        public void OpenScreenshot(byte[] pngData, VirtualAttachment originalAttachment = null)
        {
            if (pngData == null || pngData.Length == 0)
            {
                Debug.LogWarning("Cannot open screenshot: PNG data is null or empty");
                return;
            }

            // Call the static method on EditScreenCaptureWindow with the original attachment
            EditScreenCaptureWindow.OpenWithScreenshot(pngData, originalAttachment);
        }
    }
}
