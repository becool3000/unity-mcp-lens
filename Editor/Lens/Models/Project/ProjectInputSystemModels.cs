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
        public bool IncludeCompatibilitySignals { get; set; }
        public bool IncludeDetails { get; set; }
        public int MaxItems { get; set; } = 8;
    }

    sealed class PackageCompatibilityRequest
    {
        public string PackageName { get; set; }
        public string ExpectedVersion { get; set; }
        public bool IncludeEditorLogSignals { get; set; }
        public bool IncludeAssemblySignals { get; set; } = true;
        public int MaxItems { get; set; } = 8;
    }

    sealed class InputActionsInspectRequest
    {
        public string AssetPath { get; set; }
        public bool IncludeBindings { get; set; }
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

    sealed class ProjectDiagnosticIssue
    {
        public string severity { get; set; }
        public string code { get; set; }
        public string message { get; set; }
    }

    sealed class ProjectPackageInfo
    {
        public string requestedName { get; set; }
        public bool installed { get; set; }
        public string manifestVersion { get; set; }
        public string registeredVersion { get; set; }
        public string packageId { get; set; }
        public string assetPath { get; set; }
        public string source { get; set; }
        public bool isEmbedded { get; set; }
        public bool isLocal { get; set; }
    }

    sealed class ProjectEditorInfo
    {
        public string unityVersion { get; set; }
        public string platform { get; set; }
    }

    sealed class ProjectAssemblySignal
    {
        public string name { get; set; }
        public bool loaded { get; set; }
        public bool typeLoadOk { get; set; }
        public string version { get; set; }
        public string location { get; set; }
        public string sourcePath { get; set; }
        public string errorType { get; set; }
        public string error { get; set; }
        public int loaderErrorCount { get; set; }
    }

    sealed class ProjectAssemblySignalsResult
    {
        public int count { get; set; }
        public int returned { get; set; }
        public ProjectAssemblySignal[] assemblies { get; set; } = new ProjectAssemblySignal[0];
    }

    sealed class ProjectEditorLogSignal
    {
        public int index { get; set; }
        public string message { get; set; }
    }

    sealed class ProjectEditorLogSignalsResult
    {
        public string path { get; set; }
        public bool exists { get; set; }
        public int count { get; set; }
        public ProjectEditorLogSignal[] signals { get; set; } = new ProjectEditorLogSignal[0];
    }

    sealed class ProjectCompatibilitySignals
    {
        public string status { get; set; }
        public List<ProjectDiagnosticIssue> issues { get; set; } = new();
    }

    sealed class InputActionBindingRow
    {
        public string name { get; set; }
        public string path { get; set; }
        public string action { get; set; }
        public string groups { get; set; }
        public string interactions { get; set; }
        public string processors { get; set; }
        public bool? isComposite { get; set; }
        public bool? isPartOfComposite { get; set; }
    }

    sealed class InputActionWrapperGeneration
    {
        public bool available { get; set; }
        public string importerType { get; set; }
        public bool? generateWrapperCode { get; set; }
        public string wrapperClassName { get; set; }
        public string wrapperCodePath { get; set; }
        public List<ProjectDiagnosticIssue> issues { get; set; } = new();
    }

    sealed class InputActionsInspectResult
    {
        public string path { get; set; }
        public bool exists { get; set; }
        public int mapCount { get; set; }
        public int actionCount { get; set; }
        public int bindingCount { get; set; }
        public int controlSchemeCount { get; set; }
        public InputActionWrapperGeneration wrapperGeneration { get; set; }
        public InputActionBindingRow[] bindings { get; set; } = new InputActionBindingRow[0];
        public List<ProjectDiagnosticIssue> issues { get; set; } = new();
    }

    sealed class InputActionAssetsSummary
    {
        public int count { get; set; }
        public int returned { get; set; }
        public InputActionsInspectResult[] assets { get; set; } = new InputActionsInspectResult[0];
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
