using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementBlockAnswer : ChatElementBlockMarkdown<AnswerBlockModel>
    {
        VisualElement m_TextFieldRoot;
        Foldout m_SourcesFoldout;
        VisualElement m_SourcesContent;

        protected override void InitializeView(TemplateContainer view)
        {
            base.InitializeView(view);

            m_TextFieldRoot = view.Q<VisualElement>("textFieldRoot");

            m_SourcesFoldout = view.Q<Foldout>("sourcesFoldout");
            m_SourcesFoldout.RegisterValueChangedCallback(_ => OnSourcesFoldoutChanged());
            m_SourcesContent = view.Q<VisualElement>("sourcesContent");
        }

        protected override void OnBlockModelChanged()
        {
            BuildMarkdownChunks(BlockModel.Content, BlockModel.IsComplete);

            RefreshText(m_TextFieldRoot);
            RefreshSourceBlocks();
        }

        void RefreshSourceBlocks()
        {
            if (!BlockModel.IsComplete || m_SourceBlocks == null || m_SourceBlocks.Count == 0)
            {
                m_SourcesFoldout.style.display = DisplayStyle.None;
                return;
            }

            m_SourcesFoldout.style.display = DisplayStyle.Flex;
            m_SourcesContent.Clear();

            for (var index = 0; index < m_SourceBlocks.Count; index++)
            {
                var sourceBlock = m_SourceBlocks[index];
                var entry = new ChatElementSourceEntry();
                entry.Initialize(Context);
                entry.SetData(index, sourceBlock);
                m_SourcesContent.Add(entry);
            }
        }

        void OnSourcesFoldoutChanged()
        {
            EditorTask.delayCall += Context.SendScrollToEndRequest;
        }
    }
}
