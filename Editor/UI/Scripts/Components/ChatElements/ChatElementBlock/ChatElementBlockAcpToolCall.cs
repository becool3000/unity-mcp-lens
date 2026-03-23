using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using Unity.AI.Assistant.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementBlockAcpToolCall : ChatElementBlockBase<AcpToolCallBlockModel>
    {
        VisualElement m_RootContainer;
        AcpToolCallElement m_ToolCallElement;
        PermissionElement m_PermissionElement;

        // Track the request ID we've created a permission element for to avoid duplicates
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
                    return BlockModel.LatestUpdate.Status != AcpToolCallStatus.Pending;

                return BlockModel.CallInfo?.Status != AcpToolCallStatus.Pending;
            }
        }

        public override void OnConversationCancelled()
        {
            m_ToolCallElement?.OnConversationCancelled();

            // Cancel any pending permission
            if (m_PermissionElement != null && BlockModel.HasPendingPermission)
            {
                m_PermissionElement.CancelInteraction();
            }
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
                m_ToolCallElement = new AcpToolCallElement();
                m_ToolCallElement.Initialize(Context);
                m_RootContainer.Add(m_ToolCallElement);
            }

            // Always update with the latest CallInfo
            m_ToolCallElement.OnToolCall(BlockModel.CallInfo);

            // Cache renderable rawInput from pending permission (do this once).
            // We cache it because PendingPermission is cleared when the user responds,
            // but we still need to know whether to hide the default details.
            if (m_CachedRenderableRawInput == null && BlockModel.PendingPermission != null)
            {
                var rawInput = BlockModel.PendingPermission.ToolCall?.RawInput;
                if (PermissionContentRendererRegistry.GetRenderer(rawInput) != null)
                {
                    m_CachedRenderableRawInput = rawInput;
                }
            }

            // If we have (or had) renderable content, hide the default details
            // (the content is shown properly in the permission element instead)
            if (m_CachedRenderableRawInput != null)
            {
                m_ToolCallElement.HideDetails();
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

            // Check if we need to create a new permission element
            if (BlockModel.HasPendingPermission)
            {
                // Only create if this is a new request (different request ID)
                if (m_CurrentPermissionRequestId == null ||
                    !m_CurrentPermissionRequestId.Equals(pendingPermission.RequestId))
                {
                    CreatePermissionElement(pendingPermission);
                }
            }
            else if (BlockModel.PermissionResponse != null)
            {
                var displayRequest = pendingPermission ?? CreateFallbackPermissionRequest();
                if (displayRequest != null &&
                    (m_PermissionElement == null ||
                     (m_CurrentPermissionRequestId != null && !m_CurrentPermissionRequestId.Equals(displayRequest.RequestId))))
                {
                    CreatePermissionElement(displayRequest);
                }

                if (m_PermissionElement == null)
                    return;

                if (BlockModel.PermissionResponse.Outcome == "cancelled")
                {
                    m_PermissionElement.ShowCanceledState();
                    return;
                }

                if (TryResolveAnswer(BlockModel.PermissionResponse, displayRequest?.Options, out var answer))
                {
                    m_PermissionElement.ShowAnsweredState(answer);
                }
            }
        }

        void CreatePermissionElement(AcpPermissionRequest request)
        {
            if (request == null)
                return;

            // Remove old permission element if any
            if (m_PermissionElement != null)
            {
                Remove(m_PermissionElement);
                m_PermissionElement = null;
            }

            m_CurrentPermissionRequestId = request.RequestId;

            // Create the permission element with info from the request
            var action = request.ToolCall?.Title ?? "Execute tool";
            m_PermissionElement = new PermissionElement(
                action,
                pointCount: request.ToolCall?.Cost ?? 0,
                options: request.Options,
                rawInput: request.ToolCall?.RawInput);
            m_PermissionElement.Initialize(Context);

            // Wire up the response handler
            m_PermissionElement.OnCompleted += OnPermissionAnswered;

            // Add to this element (below the tool call)
            Add(m_PermissionElement);
        }

        AcpPermissionRequest CreateFallbackPermissionRequest()
        {
            if (BlockModel.CallInfo == null)
                return null;

            return new AcpPermissionRequest
            {
                RequestId = BlockModel.PermissionResponse?.OptionId ?? BlockModel.CallInfo.ToolCallId,
                ToolCall = new AcpToolCall
                {
                    ToolCallId = BlockModel.CallInfo.ToolCallId,
                    ToolName = BlockModel.CallInfo.ToolName ?? BlockModel.CallInfo.Title,
                    Title = BlockModel.CallInfo.Title ?? BlockModel.CallInfo.ToolName ?? "Execute tool"
                },
                Options = BlockModel.PendingPermission?.Options
            };
        }

        static bool TryResolveAnswer(AcpPermissionOutcome outcome, AcpPermissionOption[] options, out ToolPermissions.UserAnswer answer)
        {
            answer = ToolPermissions.UserAnswer.DenyOnce;

            if (outcome == null || outcome.Outcome != "selected")
                return false;

            if (string.IsNullOrEmpty(outcome.OptionId))
                return false;

            if (options == null)
                return false;

            foreach (var option in options)
            {
                if (option != null && option.OptionId == outcome.OptionId)
                {
                    answer = AcpPermissionMapping.ToUserAnswer(option.Kind);
                    return true;
                }
            }

            return false;
        }

        void OnPermissionAnswered(ToolPermissions.UserAnswer answer)
        {
            var toolCallId = ToolCallId;
            if (string.IsNullOrEmpty(toolCallId))
                return;

            // Send the response through the provider abstraction
            Context.API.RespondToPermission(toolCallId, answer);
        }
    }
}
