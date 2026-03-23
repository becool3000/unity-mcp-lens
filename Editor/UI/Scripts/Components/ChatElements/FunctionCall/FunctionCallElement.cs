using System;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Tools.Editor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// UI element for displaying Unity function calls in the assistant chat.
    /// Uses an IFunctionCallRenderer to customize the display based on the function type.
    /// </summary>
    class FunctionCallElement : FunctionCallBaseElement
    {
        IFunctionCallRenderer Renderer { get; }
        bool GotResult { get; set; }
        Guid CallId { get; set; }
        string FunctionId { get; set; }

        public FunctionCallElement() : this(null) { }

        public FunctionCallElement(IFunctionCallRenderer renderer)
        {
            Renderer = renderer;
        }

        protected override void InitializeContent()
        {
            ContentRoot.Add(Renderer as VisualElement);

            if (Renderer is ManagedTemplate managedTemplate)
                managedTemplate.Initialize(Context);
            if (Renderer is IAssistantUIContextAware contextAware)
                contextAware.Context = Context;
        }

        public void OnConversationCancelled()
        {
            if (CurrentState == ToolCallState.InProgress)
                OnCallError(FunctionId, CallId, "Conversation cancelled.");
        }

        public void UpdateData(AssistantFunctionCall functionCall)
        {
            if (CallId != functionCall.CallId)
            {
                // Store the call id and function id to track the state of the function call
                CallId = functionCall.CallId;
                FunctionId = functionCall.FunctionId;
                GotResult = false;

                OnCallRequest(functionCall);
            }

            if (!GotResult && functionCall.Result.IsDone)
            {
                if (functionCall.Result.HasFunctionCallSucceeded)
                    OnCallSuccess(functionCall.FunctionId, functionCall.CallId, functionCall.Result);
                else
                    OnCallError(functionCall.FunctionId, functionCall.CallId, GetErrorMessage(functionCall.Result.Result));

                GotResult = true;
            }
        }

        // Success means the call was performed without throwing any exception.
        // Internal logic to display a failed state even if the call succeeded (ex: didCompile = false) should be handled here
        void OnCallRequest(AssistantFunctionCall functionCall)
        {
            SetState(ToolCallState.InProgress);

            // Clear the existing visual tree to support element pooling and reuse.
            // Existing IFunctionCallRenderer implementations in the package build their UI
            // dynamically via Add() during lifecycle methods rather than in constructors.
            // Failing to clear here results in duplicated, stacked UI elements.
            // ManagedTemplate renderers set up their UI once in InitializeView and must not be cleared.
            if (Renderer is VisualElement rendererElement and not ManagedTemplate)
                rendererElement.Clear();

            Renderer.OnCallRequest(functionCall);

            SetTitle(Renderer.Title);
            SetDetails(Renderer.TitleDetails);

            if (Renderer.Expanded)
            {
                EnableFoldout();
                SetFoldoutExpanded(true);
            }
        }

        void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            SetState(ToolCallState.Success);
            Renderer.OnCallSuccess(functionId, callId, result);
            if (Renderer.Expanded)
                SetFoldoutExpanded(true);
            EnableFoldout();
        }

        void OnCallError(string functionId, Guid callId, string error)
        {
            SetState(ToolCallState.Failed);

            Renderer.OnCallError(functionId, callId, error);

            if (error != null)
                EnableFoldout();
        }

        static string GetErrorMessage(JToken result) => result?.Type == JTokenType.String ? result.Value<string>() : null;
    }
}
