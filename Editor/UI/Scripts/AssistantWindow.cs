using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Backend.Socket;
using Unity.AI.Assistant.Editor.Config;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Components;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    class AssistantWindow : EditorWindow, IAssistantHostWindow, IHasCustomMenu
    {
        const string k_WindowName = "Assistant";

        static Vector2 s_MinSize = new(400, 400);

        internal IAssistantProvider AssistantInstance => m_AssistantInstance;
        Assistant.Editor.Assistant m_AssistantInstance;

        internal AssistantUIContext m_Context;
        internal AssistantView m_View;
        AssistantWindowUiContainer m_AssistantWindowUiContainer;

        static IAssistantBackend s_InternalBackendOverride = null;

        public Action FocusLost { get; set; }

        [MenuItem("Window/AI/Assistant")]
        public static AssistantWindow ShowWindow()
        {
            var editor = GetWindow<AssistantWindow>();

            editor.Show();
            editor.minSize = s_MinSize;

            return editor;
        }

        void CreateGUI()
        {
            var iconPath =
                EditorGUIUtility.isProSkin
                    ? "Sparkle.png"
                    : "Sparkle_dark.png";

            var path = Path.Combine(AssistantUIConstants.BasePath, AssistantUIConstants.UIEditorPath,
                AssistantUIConstants.AssetFolder, "icons", iconPath);

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            titleContent = new GUIContent(k_WindowName, icon);

            var configuration = new AssistantConfiguration(
                backend: s_InternalBackendOverride ?? new AssistantRelayBackend());

            s_InternalBackendOverride = null;

            m_AssistantInstance = new Assistant.Editor.Assistant(configuration);

            // Create and initialize a context for this window, will be unique for every active set of assistant UI / elements
            m_Context = new AssistantUIContext(AssistantInstance);

            // Register the screenshot opener service to avoid circular dependencies
            ServiceRegistry.RegisterService<IScreenshotOpener>(new ScreenshotOpenerService());

            m_Context.WindowDockingState = () => docked;

            m_View = new AssistantView(this);

            m_View.Initialize(m_Context);
            m_View.style.flexGrow = 1;
            m_View.style.minWidth = s_MinSize.x;
            rootVisualElement.Add(m_View);

            m_View.InitializeThemeAndStyle();
            m_View.InitializeState();

            // Initialize the view to be used to display user interactions
            m_AssistantWindowUiContainer = new AssistantWindowUiContainer(m_View);
            var permissionPolicy = new SettingsPermissionsPolicyProvider();

            // TODO:
            // The only reason this cannot be configured in the constructor is because the EditorToolPermissions needs
            // an AssistantUIContext which needs a Assistant which needs a ToolInteractionAndPermissionBridge which
            // needs a EditorToolPermissions ...
            configuration.Bridge = new ToolInteractionAndPermissionBridge(
                new EditorToolPermissions(m_Context, m_AssistantWindowUiContainer, permissionPolicy),
                new ToolInteractions(m_AssistantWindowUiContainer));

            m_AssistantInstance.Reconfigure(configuration);
        }

        internal void InternalConfigureBackend(IAssistantBackend backend)
        {
            s_InternalBackendOverride = backend;

            Close();
            CreateWindow<AssistantWindow>();
        }

        void OnDestroy()
        {
            m_View?.Deinit();
            m_AssistantWindowUiContainer?.Dispose();
            m_AssistantWindowUiContainer = null;

            // TODO: Revisit workflow disposal once backend lifecycle cleanup is more robust.
            m_AssistantInstance?.Backend?.ActiveWorkflow?.Dispose();
            m_AssistantInstance = null;
        }

        void OnLostFocus()
        {
            FocusLost?.Invoke();
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
#if ASSISTANT_DEV_TOOLS_PRESENT
            menu.AddItem(new GUIContent("Assistant Dev Tools"), false, () =>
            {
                var devWindowType = Type.GetType("Unity.AI.Assistant.DeveloperTools.AssistantDevelopmentWindow, Unity.AI.Assistant.DeveloperTools");
                devWindowType?.GetMethod("ShowWindow", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            });
#endif
        }

        /// <summary>
        /// Attaches an annotated screenshot captured from annotation mode to the assistant context.
        /// This method converts the PNG bytes to a VirtualAttachment and adds it to the context.
        /// Called when the user clicks Done in the annotation toolbar.
        /// </summary>
        /// <param name="screenshotData">The PNG screenshot data as bytes</param>
        internal void AttachAnnotatedScreenshot(byte[] screenshotData, byte[] annotationsData = null)
        {
            if (screenshotData == null || screenshotData.Length == 0)
            {
                InternalLog.LogWarning("[Annotation] No screenshot data to attach");
                return;
            }

            try
            {
                // Call the GetAttachment extension method from ScreenContextUtility
                var attachment = ScreenContextUtility.GetAttachment(
                    screenshotData,
                    ImageContextCategory.Screenshot,
                    "png",
                    annotationsData
                );

                if (attachment != null)
                {
                    attachment.DisplayName = "Annotated Screenshot";
                    attachment.Type = "Image";

                    m_Context.Blackboard.AddVirtualAttachment(attachment);
                    m_Context.VirtualAttachmentAdded?.Invoke(attachment);
                }
            }
            catch (System.Exception ex)
            {
                InternalLog.LogError($"[Annotation] Failed to attach annotated screenshot: {ex.Message}");
            }
        }

        internal void ReplaceScreenshot(VirtualAttachment originalAttachment, byte[] annotatedScreenshotData, byte[] annotationsData = null)
        {
            if (originalAttachment == null)
            {
                InternalLog.LogWarning("[Annotation] Original attachment is null, cannot replace");
                AttachAnnotatedScreenshot(annotatedScreenshotData, annotationsData);
                return;
            }

            if (annotatedScreenshotData == null || annotatedScreenshotData.Length == 0)
            {
                InternalLog.LogWarning("[Annotation] No screenshot data to use for replacement");
                return;
            }

            try
            {
                // Check if the original attachment had an existing annotations mask and merge if needed
                byte[] mergedAnnotationsData = annotationsData;
                if (originalAttachment.Metadata is ImageContextMetaData oldMeta && annotationsData != null)
                {
                    mergedAnnotationsData = AnnotationMaskUtility.MergeAnnotationMasks(oldMeta, annotationsData);
                }

                // Process the annotated screenshot and annotations data to get a new attachment
                // We reuse GetAttachment to process the data and handle scaling consistently
                var newAttachment = ScreenContextUtility.GetAttachment(
                    annotatedScreenshotData,
                    ImageContextCategory.Screenshot,
                    "png",
                    mergedAnnotationsData
                );

                if (newAttachment == null || string.IsNullOrEmpty(newAttachment.Payload))
                {
                    InternalLog.LogWarning("[Annotation] Failed to process annotated screenshot to base64");
                    return;
                }

                // Make sure we keep the correct type and display name
                newAttachment.Type = originalAttachment.Type;
                newAttachment.DisplayName = originalAttachment.DisplayName;
                // GetAttachment created new metadata which includes the annotations mask info

                // Find and replace the original attachment in the context
                // We need to find the corresponding context entry and replace it
                if (m_View != null)
                {
                    m_View.ReplaceContextScreenshot(originalAttachment, newAttachment);
                }

                // Replace the attachment in the blackboard (without triggering callbacks)
                // This updates the attachment for sending to the server without creating a duplicate in the UI
                m_Context.Blackboard.ReplaceVirtualAttachment(originalAttachment, newAttachment);
            }
            catch (System.Exception ex)
            {
                InternalLog.LogError($"[Annotation] Failed to replace screenshot: {ex.Message}");
            }
        }

        /// <summary>
        /// Provides safe access to virtual attachments in the assistant context.
        /// Safer alternative to directly accessing m_Context that won't break with internal structure changes.
        /// </summary>
        /// <returns>Read-only collection of virtual attachments, or null if context is not initialized</returns>
        internal IReadOnlyCollection<VirtualAttachment> GetVirtualAttachments()
        {
            return m_Context?.Blackboard?.VirtualAttachments;
        }
    }
}
