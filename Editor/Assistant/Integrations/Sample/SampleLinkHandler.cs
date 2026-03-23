using Unity.AI.Assistant.Editor;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Integrations.Sample.Editor
{
    [LinkHandler("sample")]
    class SampleLinkHandler : ILinkHandler
    {
        public void Handle(ILinkHandler.Context context, string prefix, string url)
        {
            EditorUtility.DisplayDialog(
                "Link Clicked",
                $"Clicked link: '{url}' with prefix '{prefix}' in conversation '{context.ConversationId}'",
                "OK"
            );
        }
    }
}
