using System;
using System.Text;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Editor.Analytics;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using TextOverflow = UnityEngine.UIElements.TextOverflow;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class SelectionElement : ManagedListEntry
    {
        Label m_Text;
        Label m_Path;
        Button m_FindButton;
        VisualElement m_Checkmark;
        Image m_PreviewImage;
        AssistantImage m_LogTypeImage;
        SelectionPopup m_Owner;
        LogData m_LogData;

        SelectionPopup.ListEntry m_Entry;

        public Action<SelectionElement> OnAddRemoveButtonClicked;
        bool m_IsSelected;
        bool m_IgnoreNextClick;
        string m_CurrentLogIconClass;

        readonly string k_PrefabInSceneStyleClass = "mui-chat-selection-prefab-text-color";
        readonly string k_EntrySelectedClass = "mui-selected-list-entry";
        readonly string k_LogPathString = "Selected Console Log";
        readonly string k_SelectionElementEvenRow = "mui-selection-element-even-row";

        protected override void InitializeView(TemplateContainer view)
        {
            m_Text = view.Q<Label>("selectionElementText");
            m_Path = view.Q<Label>("selectionElementPath");
            m_Text.enableRichText = false;
            m_Text.displayTooltipWhenElided = false;
            m_FindButton = view.SetupButton("selectionElementFindButton", OnFindClicked);
            m_Checkmark = view.Q<VisualElement>("mui-selection-element-checkmark");
            m_PreviewImage = view.Q<Image>("selectionElementPreview");
            m_LogTypeImage = view.SetupImage("selectionElementLogTypeIcon");

            m_FindButton.visible = false;
            m_FindButton.focusable = false;

            m_Text.style.overflow = Overflow.Hidden;
            m_Text.style.whiteSpace = WhiteSpace.NoWrap;

            view.RegisterCallback<ClickEvent>(ToggleSelection);
            view.RegisterCallback<MouseEnterEvent>(MouseEntered);
            view.RegisterCallback<MouseLeaveEvent>(MouseLeft);
        }

        void MouseEntered(MouseEnterEvent evt)
        {
            if (!m_IsSelected)
                m_FindButton.visible = true;
        }

        void MouseLeft(MouseLeaveEvent evt)
        {
            if (!m_IsSelected)
                m_FindButton.visible = false;
        }

        void ToggleSelection(ClickEvent evt)
        {
            if (m_IgnoreNextClick)
            {
                m_IgnoreNextClick = false;
                return;
            }

            if (m_Entry.LogData.HasValue)
            {
                m_Owner.SelectedLogReference(m_Entry.LogData.Value, this);
                AIAssistantAnalytics.ReportContextChooseContextFromFlyoutEvent(m_Entry.LogData.Value);
            }
            else
            {
                m_Owner.SelectedObject(m_Entry.Object, this);
                AIAssistantAnalytics.ReportContextChooseContextFromFlyoutEvent(m_Entry.Object);
            }
        }

        public override void SetData(int index, object data, bool isSelected = false)
        {
            base.SetData(index, data, isSelected);

            m_Entry = data as SelectionPopup.ListEntry;
            SetOwner(m_Entry?.Owner);

            if (m_Entry == null)
            {
                return;
            }

            // For background coloring
            SetAsEvenRow(index % 2 == 0);

            if (m_Entry.LogData != null)
            {
                var useTimestamp = ConsoleUtils.IsConsoleShowingTimestamps() && m_Entry.LogData.Value.MessageWithTimestamp != null;
                var message = useTimestamp
                    ? m_Entry.LogData.Value.MessageWithTimestamp
                    : m_Entry.LogData.Value.Message;

                var newlineIndex = message.IndexOfAny(new[] { '\r', '\n' });
                var firstLine = newlineIndex >= 0 ? message.Substring(0, newlineIndex) : message;
                SetText(firstLine);

                if (m_Entry.LogData.Value.IsSelectedInConsole)
                    SetPath(k_LogPathString);
                else
                    m_Path.SetDisplay(false);

                string[] lines = m_Entry.LogData.Value.Message.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

                if (lines.Length > 0)
                    m_Text.tooltip = $"Console {m_Entry.LogData.Value.Type}:\n{lines[0]}";

                m_Text.style.textOverflow = TextOverflow.Ellipsis;

                var logMode = m_Entry.LogData.Value.Type;
                var newLogIconClass = LogUtils.GetLogIconClassName(logMode);

                if (m_CurrentLogIconClass != newLogIconClass)
                {
                    m_LogTypeImage.SetIconClassName(newLogIconClass);
                    m_CurrentLogIconClass = newLogIconClass;
                }

                m_LogTypeImage.SetDisplay(true);

                m_PreviewImage.SetDisplay(false);
            }
            else
            {
                var unityObj = m_Entry.Object;

                if (unityObj != null)
                {
                    SetText(unityObj.name);
                    string path = GetObjectPath(unityObj);
                    SetPath(path);
                    ShowTextAsPrefabInScene(unityObj.IsPrefabInScene());

                    m_Text.tooltip = ContextUtils.GetObjectTooltip(unityObj);

                    m_PreviewImage.image = GetAssetPreview(unityObj);
                    m_PreviewImage.SetDisplay(true);

                    m_LogTypeImage.SetDisplay(false);
                }

                m_Text.style.textOverflow = TextOverflow.Clip;
            }

            SetSelected(m_Entry.IsSelected);
        }

        static string GetObjectPath(UnityEngine.Object obj)
        {
            if (AssetDatabase.Contains(obj))
                return AssetDatabase.GetAssetOrScenePath(obj);

            if (obj is GameObject go)
                return GetGameObjectPath(go);

            return string.Empty;
        }

        static string GetGameObjectPath(GameObject go)
        {
            var pathBuilder = new StringBuilder();

            // Build path from bottom up (child to root)
            var current = go.transform;
            while (current != null)
            {
                if (pathBuilder.Length > 0)
                    pathBuilder.Insert(0, "/");
                pathBuilder.Insert(0, current.name);
                current = current.parent;
            }

            // Prepend scene name
            var sceneName = GetSceneName(go.scene);
            if (!string.IsNullOrEmpty(sceneName))
            {
                pathBuilder.Insert(0, "/");
                pathBuilder.Insert(0, sceneName);
            }

            return pathBuilder.ToString();
        }

        static string GetSceneName(UnityEngine.SceneManagement.Scene scene)
        {
            return string.IsNullOrEmpty(scene.name) ? "Unsaved Scene" : scene.name;
        }

        static Texture2D GetAssetPreview(UnityEngine.Object obj)
        {
            if (obj is DefaultAsset && FolderContextUtils.IsFolder(obj, out _))
            {
                var folderIcon = EditorGUIUtility.IconContent("Folder Icon")?.image as Texture2D;
                if (folderIcon != null)
                    return folderIcon;
            }

            var preview = AssetPreview.GetAssetPreview(obj);
            if (preview == null)
            {
                preview = AssetPreview.GetMiniTypeThumbnail(obj.GetType());
            }

            return preview;
        }

        void SetText(string text)
        {
            m_Text.text = text;
        }

        void SetPath(string path)
        {
            m_Path.text = path;
        }

        void SetOwner(SelectionPopup selectionPopup)
        {
            m_Owner = selectionPopup;
        }

        void ShowTextAsPrefabInScene(bool isPrefab)
        {
            if (isPrefab)
                m_Text.AddToClassList(k_PrefabInSceneStyleClass);
            else
                m_Text.RemoveFromClassList(k_PrefabInSceneStyleClass);
        }

        void SetAsEvenRow(bool even)
        {
            if (even)
                AddToClassList(k_SelectionElementEvenRow);
            else
                RemoveFromClassList(k_SelectionElementEvenRow);
        }

        public void SetSelected(bool selected)
        {
            m_Checkmark.visible = selected;
            if (selected)
            {
                AddToClassList(k_EntrySelectedClass);
            }
            else
            {
                RemoveFromClassList(k_EntrySelectedClass);
            }
        }

        void OnFindClicked(PointerUpEvent evt)
        {
            m_IgnoreNextClick = true;

            if (m_Entry.LogData != null)
            {
                ConsoleUtils.SelectConsoleLog(m_Entry.LogData.Value);
            }
            else
            {
                m_Owner.PingObject(m_Entry.Object);

                AIAssistantAnalytics.ReportContextPingAttachedContextObjectFromFlyoutEvent(m_Entry.Object);
            }
        }
    }
}
