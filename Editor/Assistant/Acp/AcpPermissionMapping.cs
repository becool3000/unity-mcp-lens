using System.Linq;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Provides mapping between ACP permission options and ToolPermissions.UserAnswer.
    /// </summary>
    static class AcpPermissionMapping
    {
        /// <summary>
        /// ACP option kind for "allow once".
        /// </summary>
        public const string AllowOnceKind = "allow_once";

        /// <summary>
        /// ACP option kind for "allow always" (for this session/conversation).
        /// </summary>
        public const string AllowAlwaysKind = "allow_always";

        /// <summary>
        /// ACP option kind for "reject once".
        /// </summary>
        public const string RejectOnceKind = "reject_once";

        /// <summary>
        /// ACP option kind for "reject always" - not supported in Assistant UI.
        /// </summary>
        public const string RejectAlwaysKind = "reject_always";

        /// <summary>
        /// Converts an ACP permission option kind to the corresponding ToolPermissions.UserAnswer.
        /// </summary>
        public static ToolPermissions.UserAnswer ToUserAnswer(string acpKind)
        {
            return acpKind switch
            {
                AllowOnceKind => ToolPermissions.UserAnswer.AllowOnce,
                AllowAlwaysKind => ToolPermissions.UserAnswer.AllowAlways,
                RejectOnceKind => ToolPermissions.UserAnswer.DenyOnce,
                _ => ToolPermissions.UserAnswer.DenyOnce
            };
        }

        /// <summary>
        /// Converts a ToolPermissions.UserAnswer to the corresponding ACP option kind.
        /// </summary>
        public static string ToAcpKind(ToolPermissions.UserAnswer answer)
        {
            return answer switch
            {
                ToolPermissions.UserAnswer.AllowOnce => AllowOnceKind,
                ToolPermissions.UserAnswer.AllowAlways => AllowAlwaysKind,
                ToolPermissions.UserAnswer.DenyOnce => RejectOnceKind,
                _ => RejectOnceKind
            };
        }

        /// <summary>
        /// Finds the ACP option ID that matches the given UserAnswer.
        /// </summary>
        public static string FindOptionId(AcpPermissionOption[] options, ToolPermissions.UserAnswer answer)
        {
            if (options == null)
                return null;

            var kind = ToAcpKind(answer);
            return options.FirstOrDefault(o => o != null && o.Kind == kind)?.OptionId;
        }
    }
}
