using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementUser : ChatElementBase
    {
        VisualElement m_TextFieldRoot;
        Foldout m_ContextFoldout;
        VisualElement m_ContextContent;
        Label m_Prompt;

        protected override void InitializeView(TemplateContainer view)
        {
            m_ContextFoldout = view.Q<Foldout>("contextFoldout");
            m_ContextFoldout.SetValueWithoutNotify(false);

            m_ContextContent = view.Q<VisualElement>("contextContent");

            m_TextFieldRoot = view.Q<VisualElement>("userMessageTextFieldRoot");
        }

        /// <summary>
        /// Set the user data used by this element
        /// </summary>
        /// <param name="message">the message to display</param>
        public override void SetData(MessageModel message)
        {
            if (m_Prompt != null)
                return;

            // Validate that there is only one block and that it's a markdown block
            if (message.Blocks.Count != 1 || message.Blocks[0] is not PromptBlockModel)
                throw new System.Exception("User message should only contain one markdown block");

            var promptBlockModel = message.Blocks[0] as PromptBlockModel;
            m_Prompt = new Label
            {
                text = promptBlockModel?.Content,
                selection = { isSelectable = true },
                enableRichText = false
            };
            m_TextFieldRoot.Add(m_Prompt);
            
            Context.SearchHelper?.RegisterSearchableTextElement(m_Prompt);

            RefreshContext(message);
        }

        void RefreshContext(MessageModel message)
        {
            if (message.Context == null || message.Context.Length == 0)
            {
                m_ContextFoldout.style.display = DisplayStyle.None;
                return;
            }

            m_ContextFoldout.style.display = DisplayStyle.Flex;

            for (var index = 0; index < message.Context.Length; index++)
            {
                var contextEntry = message.Context[index];
                var entry = new ContextElement();
                entry.Initialize(Context);
                entry.SetData(contextEntry);
                entry.AddChatElementUserStyling();
                m_ContextContent.Add(entry);
            }
        }
    }
}
