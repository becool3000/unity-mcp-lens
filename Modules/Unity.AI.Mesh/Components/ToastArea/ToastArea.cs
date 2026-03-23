using System;
using System.Collections.Generic;
using Unity.AI.Mesh.Services.Stores.Selectors;
using Unity.AI.Mesh.Services.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEngine.UIElements;

namespace Unity.AI.Mesh.Components
{
    [UxmlElement]
    partial class ToastArea : VisualElement
    {
        public ToastArea() => this.UseArray(state => state.SelectGenerationFeedback(this), OnGenerationFeedbackChanged);

        void OnGenerationFeedbackChanged(IEnumerable<GenerationFeedbackData> messages)
        {
            foreach (var feedback in messages)
            {
                parent?.ShowToast(feedback.message);
                this.Dispatch(GenerationActions.removeGenerationFeedback, this.GetAsset());
            }
        }
    }
}
