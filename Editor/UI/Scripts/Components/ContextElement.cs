using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Analytics;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class ContextElement : ManagedListEntry, IContextReferenceVisualElement
    {
        const string k_TargetMissingClassName = "mui-context-element-target-missing";
        const string k_MissingObjectIconName = "mui-object-missing";
        const string k_VirtualContextIconName = "context-virtual";
        const string k_MissingComponentIconName = "mui-component-missing";
        const string k_PrefabVariantClassName = "mui-context-prefab-variant";
        const string k_PrefabClassName = "mui-context-prefab";
        const string k_ContextEditingClassName = "mui-context-element-editing";
        readonly string k_ContextDropdownListItemExtraStyle = "mui-context-item-entry";
        readonly string k_ContextDropdownListItemExtraStyleDark = "mui-context-item-entry-dark";
        readonly string k_ContextDropdownListItemExtraStyleLight = "mui-context-item-list-entry-light";
        readonly string k_ContextElementClass = "mui-context-entry-user-message";

        // Static registry to track all active ContextElements
        private static readonly List<ContextElement> s_AllElements = new();


        VisualElement m_Row;
        AssistantImage m_Icon;
        AssistantView m_Owner;
        Label m_Text;
        AssistantImage m_IconMissingOverlay;
        Button m_RemoveButton;
        Button m_ListRemoveButton;

        UnityEngine.Object m_CachedTargetObject;
        Component m_CachedTargetComponent;
        int m_LastTargetObjectNameHash;

        ContextDropdown.ContextDropdownListEntry m_Context;
        AssistantContextEntry m_ContextEntry;
        bool m_VisualRegistryRegistered;
        bool m_ContextSet;
        double m_LastClickTime;
        const double k_DoubleClickTimeThreshold = 0.3; // seconds

        public Action<AssistantContextEntry> OnRemoveCallback;

        public ContextElement() :
            base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_Row = view.Q<VisualElement>("contextRow");
            m_Icon = view.SetupImage("contextIcon");
            m_Text = view.Q<Label>("contextText");
            m_Text.enableRichText = false;
            m_Text.displayTooltipWhenElided = false;
            m_IconMissingOverlay = view.SetupImage("contextIconMissingOverlay", "close-s-trimmed");
            m_RemoveButton = view.SetupButton("removeButton", OnRemoveClicked);
            m_ListRemoveButton = view.SetupButton("removeContextElementButton", OnRemoveClicked);

            // Register click handler on the row (not buttons) for single-click ping and double-click activation
            m_Row.RegisterCallback<PointerUpEvent>(OnRowClick);

            // Register right-click context menu handler for screenshots
            m_Row.RegisterCallback<ContextClickEvent>(OnContextClick);

            RegisterAttachEvents(OnAttachToPanel, OnDetachFromPanel);
        }

        public void SetData(AssistantContextEntry contextEntry)
        {
            m_ContextEntry = contextEntry;
            m_Owner = m_Context?.Owner;
            m_ContextSet = true;

            if (!m_VisualRegistryRegistered)
            {
                RegisterContextVisualUpdate(true);
            }

            RefreshContextCache();
            RefreshUI();
        }


        public override void SetData(int index, object contextEntry, bool isSelected = false)
        {
            base.SetData(index, contextEntry);
            m_Context = contextEntry as ContextDropdown.ContextDropdownListEntry;
            m_ContextEntry = m_Context.ContextEntry;
            m_Owner = m_Context?.Owner;
            AddToClassList(k_ContextDropdownListItemExtraStyle);
            AddToClassList(index % 2 == 0
                ? k_ContextDropdownListItemExtraStyleDark
                : k_ContextDropdownListItemExtraStyleLight);

            m_ContextSet = true;

            if (!m_VisualRegistryRegistered)
            {
                RegisterContextVisualUpdate(true);
            }

            RefreshContextCache();
            RefreshUI();
        }

        internal void AddChatElementUserStyling()
        {
            AddToClassList(k_ContextElementClass);
            m_RemoveButton.SetDisplay(false);
        }

        internal void SetOwner(AssistantView owner)
        {
            m_Owner = owner;
        }

        public void RemoveListStyles(int index)
        {
            RemoveFromClassList(k_ContextDropdownListItemExtraStyle);
            RemoveFromClassList(index % 2 == 0
                ? k_ContextDropdownListItemExtraStyleDark
                : k_ContextDropdownListItemExtraStyleLight);
        }

        public void RefreshVisualElement(UnityEngine.Object activeTargetObject, Component activeTargetComponent)
        {
            // Note: we do not use `RefreshContextCache` here because it's too slow to do all the time for all elements
            //       instead we rely on the Context visual registry to do pre-check
            m_CachedTargetObject = activeTargetObject;
            m_CachedTargetComponent = activeTargetComponent;

            RefreshUI();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            RegisterContextVisualUpdate(false);
            s_AllElements.Remove(this);
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (!m_ContextSet)
            {
                // Too early, have to wait for SetData
                return;
            }

            RegisterContextVisualUpdate(true);
            s_AllElements.Add(this);

            // When a new ContextElement is added, check if there's an EditScreenCaptureWindow open
            // and if so, highlight this element if it matches the attachment being edited
            if (EditScreenCaptureWindow.Instance != null)
            {
                EditScreenCaptureWindow.Instance.TryHighlightCurrentAttachment();
            }
        }

        void RegisterContextVisualUpdate(bool register)
        {
            m_VisualRegistryRegistered = register;
            if (register)
            {
                ContextVisualElementRegistry.AddElement(m_ContextEntry, this);
            }
            else
            {
                ContextVisualElementRegistry.RemoveElement(this);
            }
        }

        void OnRowClick(PointerUpEvent evt)
        {
            // Ignore clicks on the remove buttons
            if (evt.target == m_RemoveButton || evt.target == m_ListRemoveButton ||
                (evt.target is VisualElement ve && (ve.parent == m_RemoveButton || ve.parent == m_ListRemoveButton)))
            {
                return;
            }

            if (!m_ContextSet)
            {
                return;
            }

            // For virtual attachments (screenshots), require double-click to open the editor
            if (m_ContextEntry.EntryType == AssistantContextType.Virtual)
            {
                double timeSinceLastClick = EditorApplication.timeSinceStartup - m_LastClickTime;
                m_LastClickTime = EditorApplication.timeSinceStartup;

                if (timeSinceLastClick < k_DoubleClickTimeThreshold)
                {
                    // Double-click: open the screenshot editor
                    m_ContextEntry.Activate();
                }
            }
            else
            {
                // Single click: activate the context entry (ping the asset in project/scene)
                m_ContextEntry.Activate();
            }
        }

        void OnContextClick(ContextClickEvent evt)
        {
            if (!m_ContextSet)
            {
                return;
            }

            // Only show context menu for virtual attachments (screenshots)
            if (m_ContextEntry.EntryType != AssistantContextType.Virtual)
            {
                return;
            }

            // Create context menu
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("View && Annotate"), false, () =>
            {
                m_ContextEntry.Activate();
            });
            menu.AddItem(new GUIContent("Remove"), false, () =>
            {
                m_Owner.OnRemoveContextEntry(m_ContextEntry);
                AIAssistantAnalytics.ReportContextRemoveSingleAttachedContextEvent(m_ContextEntry);
            });
            menu.ShowAsContext();

            evt.StopPropagation();
        }

        void RefreshContextCache()
        {
            switch (m_ContextEntry.EntryType)
            {
                case AssistantContextType.HierarchyObject:
                case AssistantContextType.SubAsset:
                case AssistantContextType.SceneObject:
                {
                    m_CachedTargetObject = m_ContextEntry.GetTargetObject();
                    break;
                }

                case AssistantContextType.Component:
                {
                    m_CachedTargetObject = m_ContextEntry.GetTargetObject();
                    m_CachedTargetComponent = m_ContextEntry.GetComponent();
                    break;
                }

                default:
                {
                    return;
                }
            }
        }

        void ResetMissingState()
        {
            RemoveFromClassList(k_TargetMissingClassName);
            m_Row.RemoveFromClassList(k_TargetMissingClassName);
            m_Text.SetEnabled(true);
            m_IconMissingOverlay.SetDisplay(false);
        }

        void SetAsMissing()
        {
            AddToClassList(k_TargetMissingClassName);
            m_Row.AddToClassList(k_TargetMissingClassName);
            m_Text.SetEnabled(false);
            m_IconMissingOverlay.SetDisplay(true);
            m_Icon.SetIconByTypeString(m_ContextEntry.ValueType);
        }

        void RefreshUI()
        {
            if (!IsInitialized || !visible)
            {
                return;
            }

            ResetMissingState();
            m_Text.tooltip = null;

            switch (m_ContextEntry.EntryType)
            {
                case AssistantContextType.ConsoleMessage:
                {
                    var logMode = Enum.Parse<LogDataType>(m_ContextEntry.ValueType);
                    m_Icon.SetIconClassName(LogUtils.GetLogIconClassName(logMode));

                    if (string.IsNullOrEmpty(m_ContextEntry.Value))
                    {
                        m_Text.text = "Unknown";
                    } else
                    {
                        string[] lines = m_ContextEntry.Value.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

                        if (lines.Length > 0)
                        {
                            m_Text.text = lines[0].Substring(0, Math.Min(20, lines[0].Length)) + "...";
                            m_Text.tooltip = $"Console {logMode}:\n{lines[0]}";
                        }
                    }

                    break;
                }

                case AssistantContextType.SceneObject:
                {
                    if (m_CachedTargetObject == null)
                    {
                        SetAsMissing();
                        m_Icon.SetIconClassName(k_MissingObjectIconName);
                        m_Text.text = m_ContextEntry.DisplayValue;
                    }
                    else
                    {
                        if (m_CachedTargetObject is GameObject go)
                        {
                            m_Text.EnableInClassList(k_PrefabClassName, go.IsPrefabType());
                            m_Text.EnableInClassList(k_PrefabVariantClassName, go.IsPrefabVariant());
                        }

                        m_Icon.SetIconClassName(null);
                        m_Icon.SetTexture(m_CachedTargetObject.GetTextureForObject());
                        m_Text.text = m_CachedTargetObject.name;
                    }

                    break;
                }

                case AssistantContextType.HierarchyObject:
                case AssistantContextType.SubAsset:
                {
                    if (m_CachedTargetObject == null)
                    {
                        SetAsMissing();
                        m_Icon.SetIconClassName(k_MissingObjectIconName);
                        m_Text.text = m_ContextEntry.DisplayValue;
                        m_Text.tooltip = ContextUtils.GetObjectTooltipByName(m_ContextEntry.DisplayValue, m_ContextEntry.ValueType);
                    }
                    else
                    {
                        if (m_CachedTargetObject is GameObject go)
                        {
                            m_Text.EnableInClassList(k_PrefabClassName, go.IsPrefabType());
                            m_Text.EnableInClassList(k_PrefabVariantClassName, go.IsPrefabVariant());
                        }

                        m_Icon.SetIconClassName(null);
                        m_Icon.SetTexture(m_CachedTargetObject.GetTextureForObject());
                        m_Text.text = m_CachedTargetObject.name;
                        m_Text.tooltip = ContextUtils.GetObjectTooltip(m_CachedTargetObject);
                    }

                    //ShowTextAsPrefabInScene(MessageUtils.IsPrefabInScene(unityObj));
                    break;
                }

                case AssistantContextType.Component:
                {
                    if (m_CachedTargetComponent == null)
                    {
                        SetAsMissing();
                        m_Icon.SetIconClassName(k_MissingComponentIconName);
                        m_Text.text = m_ContextEntry.DisplayValue;
                        m_Text.tooltip = ContextUtils.GetShortTypeName(m_ContextEntry.ValueType);
                    }
                    else
                    {
                        m_Icon.SetIconClassName(null);
                        m_Icon.SetTexture(m_CachedTargetComponent.GetTextureForObjectType());
                        m_Text.text = m_CachedTargetComponent.name;
                        m_Text.tooltip = m_CachedTargetComponent.GetType().Name;
                    }

                    break;
                }

                case AssistantContextType.Virtual:
                {
                    m_Icon.SetIconClassName(k_VirtualContextIconName);
                    m_Text.text = m_ContextEntry.DisplayValue;
                    m_Text.tooltip = m_ContextEntry.ValueType;

                    break;
                }

                default:
                {
                    throw new InvalidOperationException("Unhandled Context Type: " + m_ContextEntry.EntryType);
                }
            }
        }

        void OnRemoveClicked(PointerUpEvent evt)
        {
            m_Owner.OnRemoveContextEntry(m_ContextEntry);
            AIAssistantAnalytics.ReportContextRemoveSingleAttachedContextEvent(m_ContextEntry);
        }

        /// <summary>
        /// Highlights the context row corresponding to the given VirtualAttachment with a blue background.
        /// This is used when a screenshot is opened for editing to show which context item is being edited.
        /// </summary>
        public static void HighlightScreenshotByVirtualAttachment(VirtualAttachment attachment)
        {
            if (attachment == null)
            {
                return;
            }

            // First, remove highlight from all elements (in case a different screenshot was previously highlighted)
            foreach (var element in s_AllElements)
            {
                element.m_Row?.RemoveFromClassList(k_ContextEditingClassName);
            }

            // Then, highlight the new screenshot
            foreach (var element in s_AllElements)
            {
                if (element.m_ContextEntry.EntryType == AssistantContextType.Virtual &&
                    element.m_ContextEntry.Value == attachment.Payload)
                {
                    element.m_Row?.AddToClassList(k_ContextEditingClassName);
                }
            }
        }

        /// <summary>
        /// Removes the blue highlight from the context row corresponding to the given VirtualAttachment.
        /// This is used when the screenshot editing window is closed.
        /// </summary>
        public static void RemoveHighlightByVirtualAttachment(VirtualAttachment attachment)
        {
            if (attachment == null)
            {
                return;
            }

            foreach (var element in s_AllElements)
            {
                if (element.m_ContextEntry.EntryType == AssistantContextType.Virtual &&
                    element.m_ContextEntry.Value == attachment.Payload)
                {
                    element.m_Row?.RemoveFromClassList(k_ContextEditingClassName);
                }
            }
        }
    }
}
