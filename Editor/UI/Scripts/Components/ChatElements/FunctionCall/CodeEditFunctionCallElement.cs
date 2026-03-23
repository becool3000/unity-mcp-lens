using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using Unity.AI.Assistant.Editor.CodeAnalyze;
using Unity.AI.Assistant.Editor.FunctionCalling;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Tools.Editor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    [FunctionCallRenderer(typeof(CodeEditTool), nameof(CodeEditTool.SaveCode), Emphasized = true)]
    class CodeEditFunctionCallElement : VisualElement, IFunctionCallRenderer, IAssistantUIContextAware
    {
        const string k_FunctionDisplayName = "Code Edit";
        static readonly bool k_EnablePreview = false;

        public virtual string Title => k_FunctionDisplayName;
        public virtual string TitleDetails { get; private set; }
        public virtual bool Expanded => true;
        public AssistantUIContext Context { get; set; }

        CodeBlockElement m_CodeDiff;

        public CodeEditFunctionCallElement()
        {
            if (parent is ScrollView scrollView)
            {
                var contentParent = new VisualElement();
                contentParent.AddToClassList("function-call-content-scroll");
                scrollView.parent.Insert(scrollView.parent.IndexOf(scrollView), contentParent);
                contentParent.Add(this);
                scrollView.RemoveFromHierarchy();
            }
        }
        
        public void OnCallRequest(AssistantFunctionCall functionCall)
        {
            TitleDetails = functionCall.Parameters?["description"]?.ToString();
            
            m_CodeDiff = CreateContent(functionCall);
        }

        public void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
          /* Compilation error display is currently disabled
          
           var typedResult = result.GetTypedResult<CodeEditTool.CodeEditOutput>();
            
            // If there is a compilation error, show the error after the code
            if (!string.IsNullOrEmpty(typedResult.CompilationOutput))
            {
                 Uncomment when code highlight is fixed to show error
                var errors = GetErrorsFromCompilationOutput(typedResult.CompilationOutput);
                m_CodeDiff.DisplayErrors(errors);
            }
            */
        }

        public void OnCallError(string functionId, Guid callId, string error)
        {
            Add(FunctionCallUtils.CreateContentLabel(error));
        }


        CodeBlockElement CreateContent(AssistantFunctionCall functionCall)
        {
            functionCall.GetCodeEditParameters(out var filePath, out var newCode, out var oldCode);

            var uiPreview = k_EnablePreview ? FunctionCallUIPreviewHandler.TryCreatePreview(Context, filePath, newCode, oldCode) : null;

            if (uiPreview != null)
            {
                var tabbedContainer = new CodeEditTabbedContent();
                tabbedContainer.Initialize(Context, autoShowControl: false);

                var codeDiff = CreateCodeDiff(newCode, oldCode, filePath);
                tabbedContainer.SetContent(codeDiff, uiPreview);
                
                Add(tabbedContainer);
                return codeDiff;
            }
            else
            {
                var codeDiff = CreateCodeDiff(newCode, oldCode, filePath);
                Add(codeDiff);
                return codeDiff;
            }
        }

        CodeBlockElement CreateCodeDiff(string newCode, string oldCode, string filePath)
        {
            var codeDiff = new CodeBlockElement();
            codeDiff.Initialize(Context);

            if (!string.IsNullOrEmpty(newCode) || !string.IsNullOrEmpty(oldCode))
            {
                codeDiff.SetCode(newCode, oldCode);
            }

            if (filePath != null)
            {
                var filename = Path.GetFileName(filePath);
                codeDiff.SetCustomTitle(filename);
                codeDiff.SetFilename(filename);
            }
            else
            {
                codeDiff.SetCustomTitle("");
                codeDiff.SetFilename("");
            }

            codeDiff.ShowSaveButton(false);

            return codeDiff;
        }

        CompilationErrors GetErrorsFromCompilationOutput(string compilationOutput)
        {
            var errors = new CompilationErrors();
            var lines = compilationOutput.Split(new[] { AssistantConstants.NewLineCRLF, AssistantConstants.NewLineLF }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                var error = CompilationErrorUtils.Parse(line);
                errors.Add(error.Message, error.Line);
            }

            return errors;
        }
    }
}
