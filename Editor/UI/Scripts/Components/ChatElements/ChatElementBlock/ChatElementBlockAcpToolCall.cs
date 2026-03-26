using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.UserInteraction;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementBlockAcpToolCall : ChatElementBlockBase<AcpToolCallBlockModel>
    {
        VisualElement m_RootContainer;
        AcpToolCallElement m_ToolCallElement;

        // Track the request ID we've created a queue entry for to avoid duplicates
        object m_CurrentPermissionRequestId;

        // Cache the rawInput if it has renderable content, so we can keep hiding details
        // even after PendingPermission is cleared (which happens when the user responds)
        JObject m_CachedRenderableRawInput;

        public string ToolCallId => BlockModel.CallInfo?.ToolCallId;

        public bool IsDone
        {
            get
            {
                // Check LatestUpdate first as it has the most recent status
                if (BlockModel.LatestUpdate != null)
                {
                    return BlockModel.LatestUpdate.Status != AcpToolCallStatus.Pending;
                }

                return BlockModel.CallInfo?.Status != AcpToolCallStatus.Pending;
            }
        }

        public override void OnConversationCancelled()
        {
            m_ToolCallElement?.OnConversationCancelled();
        }

        protected override void InitializeView(TemplateContainer view)
        {
            base.InitializeView(view);

            m_RootContainer = view.Q<VisualElement>("functionCallRoot");
        }

        protected override void OnBlockModelChanged()
        {
            RefreshContent();
            RefreshPermission();
        }

        void RefreshContent()
        {
            if (m_ToolCallElement == null)
            {
                var renderer = AcpToolCallRendererFactory.TryCreate(BlockModel.CallInfo?.ToolName);
                m_ToolCallElement = new AcpToolCallElement(renderer);
                m_ToolCallElement.Initialize(Context);
                m_RootContainer.Add(m_ToolCallElement);
            }

            // Always update with the latest CallInfo
            m_ToolCallElement.OnToolCall(BlockModel.CallInfo);

            // Skip diff content handling when a custom renderer is active — it handles its own content
            if (!m_ToolCallElement.HasRenderer)
            {
                // Cache diff rawInput from pending permission (do this once).
                // We cache it because PendingPermission is cleared when the user responds,
                // but we still need to know whether to hide the default details.
                // Only cache when it's a diff (file write/edit) — other renderers like
                // MarkdownPermissionContentRenderer are handled by the PermissionElement itself.
                if (m_CachedRenderableRawInput == null && BlockModel.PendingPermission != null)
                {
                    var rawInput = BlockModel.PendingPermission.ToolCall?.RawInput;
                    if (IsDiffRawInput(rawInput))
                    {
                        m_CachedRenderableRawInput = rawInput;
                    }
                }

                // Auto-approved: no permission showed content, but rawInput has file data.
                // Only use this when completed to avoid showing content before the write finishes.
                if (m_CachedRenderableRawInput == null && IsDone && BlockModel.RawInput != null)
                {
                    if (IsDiffRawInput(BlockModel.RawInput))
                    {
                        m_CachedRenderableRawInput = BlockModel.RawInput;
                    }
                }

                // If we have diff content, render it inline in the tool call element
                if (m_CachedRenderableRawInput != null)
                {
                    m_ToolCallElement.SetDiffContent(m_CachedRenderableRawInput, Context);
                }
            }

            // If we have an update, apply it as well
            if (BlockModel.LatestUpdate != null)
            {
                m_ToolCallElement.OnToolCallUpdate(BlockModel.LatestUpdate);
            }
        }

        void RefreshPermission()
        {
            var pendingPermission = BlockModel.PendingPermission;

            // Check if we need to enqueue a new permission entry
            if (BlockModel.HasPendingPermission)
            {
                // Only enqueue if this is a new request (different request ID)
                if (m_CurrentPermissionRequestId == null ||
                    !m_CurrentPermissionRequestId.Equals(pendingPermission.RequestId))
                {
                    EnqueuePermission(pendingPermission);
                }
            }
        }

        void EnqueuePermission(AcpPermissionRequest request)
        {
            m_CurrentPermissionRequestId = request.RequestId;

            var action = request.ToolCall?.Title ?? "Execute tool";
            var toolCallId = ToolCallId;

            var allowOption = request.Options?.FirstOrDefault(o => o?.Kind == AcpPermissionMapping.AllowOnceKind);
            var denyOption = request.Options?.FirstOrDefault(o => o?.Kind == AcpPermissionMapping.RejectOnceKind);

            var content = new ApprovalInteractionContent();
            content.SetApprovalData(allowOption?.Name, denyOption?.Name, answer =>
            {
                if (!string.IsNullOrEmpty(toolCallId))
                {
                    Context.API.RespondToPermission(toolCallId, answer);
                }
            });

            var entry = new UserInteractionEntry
            {
                Title = action,
                ContentView = content,
                OnCancel = () =>
                {
                    if (!string.IsNullOrEmpty(toolCallId))
                    {
                        Context.API.RespondToPermission(toolCallId, ToolPermissions.UserAnswer.DenyOnce);
                    }
                }
            };

            Context.InteractionQueue.Enqueue(entry);
        }

        /// <summary>
        /// Returns true if the rawInput contains file write/edit fields that SetDiffContent can render.
        /// </summary>
        static bool IsDiffRawInput(JObject rawInput)
        {
            return rawInput?["new_string"] != null || rawInput?["content"] != null;
        }

    }
}
