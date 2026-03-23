using System;
using Unity.AI.Assistant.Editor.Analytics;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class EditorToolPermissions : ToolPermissions
    {
        readonly AssistantUIContext k_Context;

        public EditorToolPermissions(AssistantUIContext context, IToolUiContainer toolUiContainer, IPermissionsPolicyProvider policyProvider) : base(toolUiContainer, policyProvider)
        {
            k_Context = context;
        }

        protected override void OnPermissionResponse(
            ToolExecutionContext.CallInfo callInfo,
            UserAnswer answer,
            PermissionType permissionType)
        {
            AIAssistantAnalytics.ReportUITriggerLocalPermissionResponseEvent(k_Context.Blackboard.ActiveConversation.Id, callInfo, answer, permissionType);
        }

        protected override IUserInteraction<UserAnswer> CreateAssetGenerationElement(ToolExecutionContext.CallInfo callInfo, string path, Type type, long cost)
        {
            var element = new PermissionElement(
                action: $"Generate {type.Name} asset",
                question: $"Save to {path}?",
                null,
                cost
            );
            element.Initialize(k_Context);
            return element;
        }

        protected override IUserInteraction<UserAnswer> CreateCodeExecutionElement(ToolExecutionContext.CallInfo callInfo, string code)
        {
            var element = new PermissionElement(action: "Execute code", code: code);
            element.Initialize(k_Context);
            return element;
        }

        protected override IUserInteraction<UserAnswer> CreateFileSystemAccessElement(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, string path)
        {
            var action = PathUtils.IsFilePath(path)
                ? operation switch
                {
                    IToolPermissions.ItemOperation.Read => "Read file from disk",
                    IToolPermissions.ItemOperation.Create => "Create file",
                    IToolPermissions.ItemOperation.Delete => "Delete file",
                    IToolPermissions.ItemOperation.Modify => "Save file",
                    _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
                }
                : operation switch
                {
                    IToolPermissions.ItemOperation.Read => "Read from disk",
                    IToolPermissions.ItemOperation.Create => "Create directory",
                    IToolPermissions.ItemOperation.Delete => "Delete directory",
                    IToolPermissions.ItemOperation.Modify => "Change directory",
                    _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
                };

            var question = operation switch
            {
                IToolPermissions.ItemOperation.Read => $"Read from {path}?",
                IToolPermissions.ItemOperation.Create => $"Write to {path}?",
                IToolPermissions.ItemOperation.Delete => $"Delete {path}?",
                IToolPermissions.ItemOperation.Modify => $"Write to {path}?",
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };

            var element = new PermissionElement(action: action, question: question);
            element.Initialize(k_Context);
            return element;
        }

        protected override IUserInteraction<UserAnswer> CreateScreenCaptureElement(ToolExecutionContext.CallInfo callInfo)
        {
            var element = new PermissionElement(action: "Allow screen capture");
            element.Initialize(k_Context);
            return element;
        }

        protected override IUserInteraction<UserAnswer> CreateToolExecutionElement(ToolExecutionContext.CallInfo callInfo)
        {
            var element = new PermissionElement(action: "Execute tool", question: $"Execute {callInfo.FunctionId}?");
            element.Initialize(k_Context);
            return element;
        }

        protected override IUserInteraction<UserAnswer> CreatePlayModeElement(ToolExecutionContext.CallInfo callInfo, IToolPermissions.PlayModeOperation operation)
        {
            var action = operation switch
            {
                IToolPermissions.PlayModeOperation.Enter => "Enter Play Mode",
                IToolPermissions.PlayModeOperation.Exit => "Exit Play Mode",
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };

            var question = $"{action}?";

            var element = new PermissionElement(action: action, question: question);
            element.Initialize(k_Context);
            return element;
        }

        protected override IUserInteraction<UserAnswer> CreateUnityObjectAccessElement(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, Type type, UnityEngine.Object target)
        {
            var action = operation switch
            {
                IToolPermissions.ItemOperation.Read => "Read Object Data",
                IToolPermissions.ItemOperation.Create => "Create New Object",
                IToolPermissions.ItemOperation.Delete => "Delete Object",
                IToolPermissions.ItemOperation.Modify => "Modify Object",
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };

            var objectName = target != null
                ? $"{target.name} ({target.GetType().Name})"
                : type?.Name;

            var question = operation switch
            {
                IToolPermissions.ItemOperation.Read => $"Read from {objectName}?",
                IToolPermissions.ItemOperation.Create => $"Create {objectName}?",
                IToolPermissions.ItemOperation.Delete => $"Delete {objectName}?",
                IToolPermissions.ItemOperation.Modify => $"Modify {objectName}?",
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };

            var element = new PermissionElement(action: action, question: question);
            element.Initialize(k_Context);
            return element;
        }
    }
}
