using System.Collections.Generic;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Markup;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    abstract class CommandDisplayTemplate : ManagedTemplate
    {
        public class ContentGroup
        {
            public string Info = "";
            public string Content = "";
            public string Arguments = "";
            public DisplayState State;
            public string Logs = "";

            public ContentGroup() { }

            public ContentGroup(ContentGroup other)
            {
                Info = other.Info;
                Content = other.Content;
                Arguments = other.Arguments;
                State = other.State;
                Logs = other.Logs;
            }
        }
        public enum DisplayState
        {
            Success = 0,
            Fail
        }

        readonly List<ContentGroup> m_Content = new();
        readonly List<ContentGroup> m_ExtraContent = new();

        internal MessageModel m_ParentMessage;

        protected CodeBlockElement m_CodeElement;

        internal virtual void SetMessage(MessageModel message)
        {
            m_ParentMessage = message;
            OnSetMessage();

            RegisterAttachEvents(Attached, Detached);

            if (m_CodeElement?.parent != null)
            {
                Context.SearchHelper.RegisterAdditionalMessageText(
                    m_ParentMessage.Id,
                    m_CodeElement,
                    AssistantConstants.GetDisclaimerHeader());
            }
        }

        protected virtual void Detached(DetachFromPanelEvent evt)
        {
            if (m_CodeElement?.parent != null)
            {
                Context.SearchHelper.UnregisterAdditionalMessageText(m_ParentMessage.Id, m_CodeElement);
            }
        }

        protected virtual void Attached(AttachToPanelEvent evt)
        {
            if (m_CodeElement?.parent != null)
            {
                Context.SearchHelper.RegisterAdditionalMessageText(
                    m_ParentMessage.Id,
                    m_CodeElement,
                    AssistantConstants.GetDisclaimerHeader());
            }
        }

        public virtual void OnSetMessage() { }
        protected CommandDisplayTemplate(System.Type customElementType, string basePath = null) : base(customElementType, basePath) { }
        protected CommandDisplayTemplate(string basePath = null, string subPath = null) : base(basePath, subPath) { }
        public string Fence { get; set; }

        protected override void InitializeView(TemplateContainer view)
        {
            m_CodeElement = new CodeBlockElement();
            m_CodeElement.Initialize(Context);
        }

        public void SetContent(FencedContent content)
        {
            m_Content.Clear();
            m_Content.Add(new ContentGroup
            {
                Content = content.Content,
                Info = content.Info,
                Arguments = content.Arguments
            });
        }

        public void SetExtraContent(IList<FencedContent> extraContent)
        {
            m_ExtraContent.Clear();
            foreach (var content in extraContent)
            {
                m_ExtraContent.Add(new ContentGroup
                {
                    Content = content.Content,
                    Info = content.Info,
                    Arguments = content.Arguments
                });
            }
        }

        public void AddContent(string content)
        {
            m_Content.Add(new ContentGroup { Content = content });
        }

        public List<ContentGroup> ContentGroups => m_Content;
        public List<ContentGroup> ExtraContent => m_ExtraContent;

        public bool Validate(int index)
        {
            if (index < 0 || index >= m_Content.Count)
            {
                Debug.LogWarning("Invalid index supplied");
                return false;
            }

            var group = m_Content[index];
            var valid = ValidateInternal(index, out group.Logs);
            group.State = valid ? DisplayState.Success : DisplayState.Fail;
            return valid;
        }

        protected virtual bool ValidateInternal(int index, out string logs)
        {
            logs = null;
            return true;
        }

        public abstract void Display(bool isUpdate = false);

        public virtual void Sync()
        {

        }

        public virtual void SetCustomTitle(string title)
        {
        }

        public virtual void SetCodeReformatting(bool reformatCode)
        {
        }

        public virtual void SetCodeType(string codeType)
        {
        }

        public virtual void SetFilename(string filename)
        {
        }
    }
}
