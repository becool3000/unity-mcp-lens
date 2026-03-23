using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.Editor.Utils
{
    /// <summary>
    /// Service for opening screenshots in the annotation editor.
    /// </summary>
    internal interface IScreenshotOpener
    {
        /// <summary>
        /// Opens a screenshot in the annotation/edit window.
        /// </summary>
        /// <param name="pngData">PNG-encoded image data</param>
        /// <param name="originalAttachment">Optional original attachment to replace when saving</param>
        void OpenScreenshot(byte[] pngData, VirtualAttachment originalAttachment = null);
    }
}
