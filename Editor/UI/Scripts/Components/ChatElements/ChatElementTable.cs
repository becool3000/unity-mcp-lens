using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    internal class ChatElementTable : ManagedTemplate
    {
        private VisualElement m_TableRoot;
        private VisualElement[] m_Rows;

        public VisualElement GetRow(int r) => m_Rows[r];

        public ChatElementTable()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_TableRoot = view.Q<VisualElement>("tableRoot");
        }

        public void SetDimensions(int w, int h)
        {
            m_Rows = new VisualElement[h];

            m_TableRoot.Clear();

            for (int i = 0; i < h; i++)
            {
                var newRow = new VisualElement();
                newRow.AddToClassList("mui-chat-response-table-row");
                m_Rows[i] = newRow;

                m_TableRoot.Add(newRow);
            }
        }
    }
}
