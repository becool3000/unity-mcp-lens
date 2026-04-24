#nullable disable
using System.Collections.Generic;

namespace Becool.UnityMcpLens.Editor.Models.Project
{
    sealed class InputSystemDiagnosticsRequest
    {
        public string AssetPath { get; set; }
        public bool IncludeDevices { get; set; }
        public bool IncludeBindings { get; set; }
        public bool IncludeEditorLogSignals { get; set; }
        public bool IncludeDetails { get; set; }
        public int MaxItems { get; set; } = 8;
    }

    sealed class ActiveInputHandlerRequest
    {
        public string Mode { get; set; }
        public bool Save { get; set; } = true;
        public bool RequestScriptReload { get; set; }
    }

    sealed class ProjectValidationMessage
    {
        public string severity { get; set; }
        public string code { get; set; }
        public string message { get; set; }
    }

    sealed class ProjectOperationResult
    {
        public bool success { get; set; }
        public string message { get; set; }
        public object data { get; set; }
        public string errorKind { get; set; }
        public object errorData { get; set; }

        public static ProjectOperationResult Ok(string message, object data = null)
        {
            return new ProjectOperationResult
            {
                success = true,
                message = message,
                data = data
            };
        }

        public static ProjectOperationResult Error(string message, string errorKind, object errorData = null)
        {
            return new ProjectOperationResult
            {
                success = false,
                message = message,
                errorKind = errorKind,
                errorData = errorData ?? new { errorKind }
            };
        }
    }

    sealed class ActiveInputHandlerState
    {
        public string mode { get; set; }
        public int rawValue { get; set; }
        public string source { get; set; }
    }

    sealed class InputHandlerPlan
    {
        public ActiveInputHandlerState current { get; set; }
        public ActiveInputHandlerState requested { get; set; }
        public bool willModify { get; set; }
        public bool restartRequired { get; set; }
        public object expectedDefines { get; set; }
        public List<ProjectValidationMessage> validationMessages { get; set; } = new();
    }
}
