using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Integrations.Sample.Editor
{
    class SampleInteraction : BaseInteraction<string>
    {
        public SampleInteraction(List<string> choices)
        {
            foreach (var choice in choices)
            {
                var button = new Button(() => CompleteInteraction(choice))
                {
                    text = choice
                };
                Add(button);
            }
        }
    }
}
