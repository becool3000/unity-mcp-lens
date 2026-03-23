using System;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.FunctionCalling
{
    class DefaultFunctionCallRenderer : VisualElement, IFunctionCallRenderer
    {
        public virtual string Title { get; protected set; }
        public virtual string TitleDetails { get; protected set; }
        public virtual bool Expanded { get; protected set; }

        public virtual void OnCallRequest(AssistantFunctionCall functionCall)
        {
            // Clear prior dynamic content to prevent UI stacking upon element reuse.
            Clear();
            Title = functionCall.GetDefaultTitle();
            TitleDetails = functionCall.GetDefaultTitleDetails();
        }

        public virtual void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            // Clear prior dynamic content to prevent UI stacking upon element reuse.
            Clear();
            Add(result.CreateDefaultContentLabel());
        }

        public virtual void OnCallError(string functionId, Guid callId, string error)
        {
            // Clear prior dynamic content to prevent UI stacking upon element reuse.
            Clear();
            Add(FunctionCallUtils.CreateContentLabel(error));
        }
    }
}
