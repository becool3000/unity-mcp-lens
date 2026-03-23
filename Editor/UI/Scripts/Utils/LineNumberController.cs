using System.Collections.Generic;
using System.Text;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Components;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    class LineNumberController
    {
        readonly Label k_SourceLabel;
        readonly Label k_TargetLabel;

        readonly IDictionary<int, CodeChangeType> k_CodeLineChangeIndicators;

        const string k_InvisibleStart = "<color=#0000>";
        const string k_InvisibleEnd = "</color>";

        static readonly string k_NoChangeIndicator = $"{k_InvisibleStart}{new string('_', 4)}{k_InvisibleEnd}";
        static readonly string k_AddedLineIndicator =
            $"<mark={AssistantUIConstants.CodeLineAddedColor}>{k_InvisibleStart}_{k_InvisibleEnd}+{k_InvisibleStart}__{k_InvisibleEnd}</mark>";
        static readonly string k_RemovedLineIndicator =
            $"<mark={AssistantUIConstants.CodeLineRemovedColor}>{k_InvisibleStart}_{k_InvisibleEnd}-{k_InvisibleStart}__{k_InvisibleEnd}</mark>";

        public LineNumberController(Label source, Label target,
            IDictionary<int, CodeChangeType> codeLineChanges)
        {
            k_SourceLabel = source;
            k_TargetLabel = target;

            k_CodeLineChangeIndicators = codeLineChanges;

            source.RegisterValueChangedCallback(_ => RefreshDisplay());
            source.RegisterCallback<GeometryChangedEvent>(_ => RefreshDisplay());
        }

        public void RefreshDisplay()
        {
            var lines = k_SourceLabel.text.Split(AssistantConstants.NewLineLF);
            var isDiffMode = k_CodeLineChangeIndicators?.Count > 0;

            if (isDiffMode)
            {
                RefreshDiffDisplay(lines);
            }
            else
            {
                RefreshSimpleDisplay();
            }
        }

        void RefreshSimpleDisplay()
        {
            k_TargetLabel.SetDisplay(false);
        }

        void RefreshDiffDisplay(string[] lines)
        {
            var builder = new StringBuilder();

            for (var i = 0; i < lines.Length; i++)
            {
                var type = k_CodeLineChangeIndicators.TryGetValue(i + 1, out var bgType)
                    ? bgType
                    : CodeChangeType.None;

                var changeIndicator = type switch
                {
                    CodeChangeType.Added => k_AddedLineIndicator,
                    CodeChangeType.Removed => k_RemovedLineIndicator,
                    _ => k_NoChangeIndicator
                };

                builder.AppendLine(changeIndicator);
            }

            k_TargetLabel.text = builder.ToString();
            k_TargetLabel.SetDisplay(true);
        }
    }
}
