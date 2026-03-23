using System;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Platform-agnostic abstraction for native screen capture.
    /// Routes calls to platform-specific implementations.
    /// </summary>
    static class NativeCapture
    {
        public static byte[] CaptureDesktopRGBA(out int width, out int height)
        {
#if UNITY_EDITOR_WIN
            return WindowsGdiCapture.CaptureMainMonitorRGBA(out width, out height);
#elif UNITY_EDITOR_OSX
            return MacCgCapture.CaptureMainMonitorRGBA(out width, out height);
#else
            width = height = 0;
            throw new PlatformNotSupportedException("Only Windows/macOS editor supported.");
#endif
        }

        public static byte[] CaptureUnityEditorWindowRGBA(out int width, out int height)
        {
#if UNITY_EDITOR_WIN
            return WindowsGdiCapture.CaptureUnityEditorWindowRGBA(out width, out height);
#elif UNITY_EDITOR_OSX
            return MacCgCapture.CaptureUnityEditorWindowRGBA(out width, out height);
#else
            width = height = 0;
            throw new PlatformNotSupportedException("Only Windows/macOS editor supported.");
#endif
        }
    }
}
