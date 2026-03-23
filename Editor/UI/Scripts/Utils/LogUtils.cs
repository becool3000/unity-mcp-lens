using Unity.AI.Assistant.Bridge.Editor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    static class LogUtils
    {
        public static string GetLogIconClassName(LogDataType logType)
        {
            switch (logType)
            {
                case LogDataType.Warning:
                    return "warn";
                case LogDataType.Error:
                    return "error";
                default:
                    return "info";
            }
        }
    }
}
