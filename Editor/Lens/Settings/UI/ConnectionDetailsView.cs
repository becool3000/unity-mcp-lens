using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Models;
using Becool.UnityMcpLens.Editor.Security;
using Becool.UnityMcpLens.Editor.Settings.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Becool.UnityMcpLens.Editor.Settings.UI
{
    /// <summary>
    /// Reusable component for displaying connection information.
    /// Used by both ConnectionApprovalDialog and ConnectionItemControl.
    /// </summary>
    class ConnectionDetailsView : VisualElement
    {
        static readonly string UxmlPath = MCPConstants.uiTemplatesPath + "/ConnectionDetailsView.uxml";
        static readonly string UssPath = MCPConstants.uiTemplatesPath + "/ConnectionDetailsView.uss";

        // UI Elements
        HelpBox m_TierWarning;
        VisualElement m_SummaryBox;
        Label m_ClientName;
        Label m_PublisherLabel;
        Label m_PathLabel;
        Label m_PreviouslyApprovedLabel;
        HelpBox m_PermissionsWarning;
        Foldout m_DetailsFoldout;

        // Connection info
        Label m_ConnectionTimestamp;

        // Server info
        Label m_ServerProcessName;
        Label m_ServerProcessId;
        Label m_ServerExecutable;
        Label m_ServerHash;
        VisualElement m_ServerSigningInfo;
        Label m_ServerCodeSigned;
        VisualElement m_ServerPublisherRow;
        Label m_ServerPublisher;
        VisualElement m_ServerSignatureRow;
        Label m_ServerSignatureValid;

        // Client info
        Label m_ClientProcessName;
        Label m_ClientProcessId;
        Label m_ClientExecutable;
        Label m_ClientHash;
        VisualElement m_ClientSigningInfo;
        Label m_ClientCodeSigned;
        VisualElement m_ClientPublisherRow;
        Label m_ClientPublisher;
        VisualElement m_ClientSignatureRow;
        Label m_ClientSignatureValid;
        VisualElement m_ClientChainRow;
        Label m_ClientChainDepth;

        // Validation info
        Label m_ValidationStatus;
        Label m_ValidationReason;

        public ConnectionDetailsView()
        {
            LoadTemplate();
            InitializeElements();
        }

        void LoadTemplate()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree != null)
            {
                visualTree.CloneTree(this);
            }

            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (stylesheet != null)
            {
                styleSheets.Add(stylesheet);
            }
        }

        void InitializeElements()
        {
            // Create and add HelpBox elements (can't be created in UXML)
            var tierWarningContainer = this.Q<VisualElement>("tierWarningContainer");
            m_TierWarning = new HelpBox("", HelpBoxMessageType.Warning);
            tierWarningContainer.Add(m_TierWarning);

            // Summary elements
            m_SummaryBox = this.Q<VisualElement>("summaryBox");
            m_ClientName = this.Q<Label>("clientLabel");
            m_ClientName.enableRichText = false; // Disable rich text for security - prevents markup injection from client-provided names
            m_PublisherLabel = this.Q<Label>("publisherLabel");
            m_PathLabel = this.Q<Label>("pathLabel");
            m_PreviouslyApprovedLabel = this.Q<Label>("previouslyApprovedLabel");

            // Create and add permissions warning HelpBox
            var permissionsWarningContainer = this.Q<VisualElement>("permissionsWarningContainer");
            m_PermissionsWarning = new HelpBox("", HelpBoxMessageType.Warning);
            permissionsWarningContainer.Add(m_PermissionsWarning);

            m_DetailsFoldout = this.Q<Foldout>("detailsFoldout");

            // Connection info
            m_ConnectionTimestamp = this.Q<Label>("connectionTimestamp");

            // Server info
            m_ServerProcessName = this.Q<Label>("serverProcessName");
            m_ServerProcessId = this.Q<Label>("serverProcessId");
            m_ServerExecutable = this.Q<Label>("serverExecutable");
            m_ServerHash = this.Q<Label>("serverHash");
            m_ServerSigningInfo = this.Q<VisualElement>("serverSigningInfo");
            m_ServerCodeSigned = this.Q<Label>("serverCodeSigned");
            m_ServerPublisherRow = this.Q<VisualElement>("serverPublisherRow");
            m_ServerPublisher = this.Q<Label>("serverPublisher");
            m_ServerSignatureRow = this.Q<VisualElement>("serverSignatureRow");
            m_ServerSignatureValid = this.Q<Label>("serverSignatureValid");

            // Client info
            m_ClientProcessName = this.Q<Label>("clientProcessName");
            m_ClientProcessId = this.Q<Label>("clientProcessId");
            m_ClientExecutable = this.Q<Label>("clientExecutable");
            m_ClientHash = this.Q<Label>("clientHash");
            m_ClientSigningInfo = this.Q<VisualElement>("clientSigningInfo");
            m_ClientCodeSigned = this.Q<Label>("clientCodeSigned");
            m_ClientPublisherRow = this.Q<VisualElement>("clientPublisherRow");
            m_ClientPublisher = this.Q<Label>("clientPublisher");
            m_ClientSignatureRow = this.Q<VisualElement>("clientSignatureRow");
            m_ClientSignatureValid = this.Q<Label>("clientSignatureValid");
            m_ClientChainRow = this.Q<VisualElement>("clientChainRow");
            m_ClientChainDepth = this.Q<Label>("clientChainDepth");

            // Validation info
            m_ValidationStatus = this.Q<Label>("validationStatus");
            m_ValidationReason = this.Q<Label>("validationReason");

            var legalDisclaimer = "This is a third-party application not controlled by Unity. You are solely responsible for evaluating appropriateness for your use and acknowledge that Unity is not responsible for any errors, malfunction or damages that may result from your use of this application. Verify whether separate terms apply to your use of this application. Proceeding indicates your agreement to these terms.";
            // Set default permissions warning text
            m_PermissionsWarning.text =
                "This application will be able to:\n" +
                "• Execute Unity Editor commands\n" +
                "• Modify scenes and GameObjects\n" +
                "• Create and edit scripts\n" +
                "• Access all project files and assets\n\n" +
                $"This connection is active. Use Revoke Access if you do not trust this application.\n\n{legalDisclaimer}";
        }

        /// <summary>
        /// Populate the view with connection information.
        /// </summary>
        public void SetConnectionInfo(ConnectionInfo info, ValidationDecision decision = null)
        {
            if (info == null)
            {
                m_ClientName.text = "Connection information unavailable";
                return;
            }

            // Determine security tier
            var tier = SecurityTierClassifier.DetermineTier(info);

            // Set tier warning
            SetTierWarning(tier, info);

            // Set summary
            SetSummary(info);

            // Set detailed information
            SetConnectionDetails(info);
            SetServerInfo(info.Server);
            SetClientInfo(info.Client, info.ClientChainDepth);

            // Set validation info if provided
            if (decision != null)
            {
                SetValidationInfo(decision);
            }
        }

        void SetTierWarning(SecurityTier tier, ConnectionInfo info)
        {
            var appName = info.Client?.ProcessName ?? "Unknown application";
            var publisherName = info.Client?.Identity?.GetDisplayName();
            var isSigned = info.Client?.Identity?.IsSigned ?? false;

            switch (tier)
            {
                case SecurityTier.Unknown:
                    m_TierWarning.messageType = HelpBoxMessageType.Error;
                    m_TierWarning.text = $"{appName} has connected. This application is unsigned or not recognized and may be dangerous!";
                    break;
                case SecurityTier.Untrusted:
                    m_TierWarning.messageType = HelpBoxMessageType.Warning;
                    m_TierWarning.text = $"{appName} has connected. This application is unsigned - proceed with caution.";
                    break;
                case SecurityTier.Trusted:
                    m_TierWarning.messageType = HelpBoxMessageType.Info;
                    if (!string.IsNullOrWhiteSpace(publisherName))
                        m_TierWarning.text = $"{appName} from {publisherName} is connected.";
                    else if (isSigned)
                        m_TierWarning.text = $"{appName} (unnamed publisher) is connected.";
                    else
                        m_TierWarning.text = $"{appName} is connected.";
                    break;
                default:
                    m_TierWarning.messageType = HelpBoxMessageType.Warning;
                    m_TierWarning.text = $"{appName} is connected to Unity.";
                    break;
            }
        }

        void SetSummary(ConnectionInfo info)
        {
            // Application name
            var clientName = info.Client?.ProcessName ?? "Unknown Application";
            m_ClientName.text = clientName;

            // Publisher info
            if (info.Client?.Identity?.IsSigned == true)
            {
                var publisherName = info.Client.Identity.GetDisplayName() ?? "Unnamed publisher";
                m_PublisherLabel.text = $"Publisher: {publisherName}";
                m_PublisherLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                m_PublisherLabel.style.display = DisplayStyle.None;
            }

            // Path
            if (info.Client?.Identity?.Path != null)
            {
                m_PathLabel.text = $"Path: {info.Client.Identity.Path}";
                m_PathLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                m_PathLabel.style.display = DisplayStyle.None;
            }

            // Previously approved indicator
            var existingRecord = ConnectionRegistry.instance.FindMatchingConnection(info);
            if (existingRecord != null && existingRecord.Status == ValidationStatus.Accepted)
            {
                m_PreviouslyApprovedLabel.text = "Previously approved (application may have been updated)";
                m_PreviouslyApprovedLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                m_PreviouslyApprovedLabel.style.display = DisplayStyle.None;
            }
        }

        void SetConnectionDetails(ConnectionInfo info)
        {
            var relativeTime = UIUtils.FormatRelativeTime(info.Timestamp);
            var absoluteTime = info.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            m_ConnectionTimestamp.text = $"{relativeTime} ({absoluteTime})";
        }

        void SetServerInfo(ProcessInfo server)
        {
            if (server == null)
            {
                m_ServerProcessName.text = "Unable to determine server process information";
                m_ServerProcessId.parent.style.display = DisplayStyle.None;
                m_ServerExecutable.parent.style.display = DisplayStyle.None;
                m_ServerHash.parent.style.display = DisplayStyle.None;
                m_ServerSigningInfo.style.display = DisplayStyle.None;
                return;
            }

            m_ServerProcessName.text = server.ProcessName ?? "Unknown";
            m_ServerProcessId.text = server.ProcessId.ToString();
            m_ServerExecutable.text = server.Identity?.Path ?? "Unknown";

            if (server.Identity != null)
            {
                // Hash
                var hashDisplay = server.Identity.SHA256Hash != null && server.Identity.SHA256Hash.Length > 16
                    ? server.Identity.SHA256Hash.Substring(0, 16) + "..."
                    : server.Identity.SHA256Hash ?? "Unknown";
                m_ServerHash.text = hashDisplay;
                m_ServerHash.parent.style.display = DisplayStyle.Flex;

                // Signing info
                if (server.Identity.IsSigned)
                {
                    m_ServerCodeSigned.text = "Yes";
                    SetLabelColor(m_ServerCodeSigned, Color.green);

                    m_ServerPublisher.text = server.Identity.SignaturePublisher ?? "Unknown";
                    m_ServerPublisherRow.style.display = DisplayStyle.Flex;

                    m_ServerSignatureValid.text = server.Identity.SignatureValid ? "Yes" : "No";
                    SetLabelColor(m_ServerSignatureValid, server.Identity.SignatureValid ? Color.green : Color.red);
                    m_ServerSignatureRow.style.display = DisplayStyle.Flex;

                    m_ServerSigningInfo.style.display = DisplayStyle.Flex;
                }
                else
                {
                    m_ServerCodeSigned.text = "No";
                    SetLabelColor(m_ServerCodeSigned, new Color(1f, 1f, 0f));
                    m_ServerPublisherRow.style.display = DisplayStyle.None;
                    m_ServerSignatureRow.style.display = DisplayStyle.None;
                    m_ServerSigningInfo.style.display = DisplayStyle.Flex;
                }
            }
            else
            {
                m_ServerHash.parent.style.display = DisplayStyle.None;
                m_ServerSigningInfo.style.display = DisplayStyle.None;
            }
        }

        void SetClientInfo(ProcessInfo client, int chainDepth)
        {
            if (client == null)
            {
                m_ClientProcessName.text = "Unable to determine client (parent may have exited or permissions denied)";
                m_ClientProcessId.parent.style.display = DisplayStyle.None;
                m_ClientExecutable.parent.style.display = DisplayStyle.None;
                m_ClientHash.parent.style.display = DisplayStyle.None;
                m_ClientSigningInfo.style.display = DisplayStyle.None;
                m_ClientChainRow.style.display = DisplayStyle.None;
                return;
            }

            m_ClientProcessName.text = client.ProcessName ?? "Unknown";
            m_ClientProcessId.text = client.ProcessId.ToString();
            m_ClientExecutable.text = client.Identity?.Path ?? "Unknown";

            if (client.Identity != null)
            {
                // Hash
                var hashDisplay = client.Identity.SHA256Hash != null && client.Identity.SHA256Hash.Length > 16
                    ? client.Identity.SHA256Hash.Substring(0, 16) + "..."
                    : client.Identity.SHA256Hash ?? "Unknown";
                m_ClientHash.text = hashDisplay;
                m_ClientHash.parent.style.display = DisplayStyle.Flex;

                // Signing info
                if (client.Identity.IsSigned)
                {
                    m_ClientCodeSigned.text = "Yes";
                    SetLabelColor(m_ClientCodeSigned, Color.green);

                    m_ClientPublisher.text = client.Identity.SignaturePublisher ?? "Unknown";
                    m_ClientPublisherRow.style.display = DisplayStyle.Flex;

                    m_ClientSignatureValid.text = client.Identity.SignatureValid ? "Yes" : "No";
                    SetLabelColor(m_ClientSignatureValid, client.Identity.SignatureValid ? Color.green : Color.red);
                    m_ClientSignatureRow.style.display = DisplayStyle.Flex;

                    m_ClientSigningInfo.style.display = DisplayStyle.Flex;
                }
                else
                {
                    m_ClientCodeSigned.text = "No";
                    SetLabelColor(m_ClientCodeSigned, new Color(1f, 1f, 0f));
                    m_ClientPublisherRow.style.display = DisplayStyle.None;
                    m_ClientSignatureRow.style.display = DisplayStyle.None;
                    m_ClientSigningInfo.style.display = DisplayStyle.Flex;
                }
            }
            else
            {
                m_ClientHash.parent.style.display = DisplayStyle.None;
                m_ClientSigningInfo.style.display = DisplayStyle.None;
            }

            // Chain depth
            if (chainDepth > 0)
            {
                m_ClientChainDepth.text = $"{chainDepth} level{(chainDepth == 1 ? "" : "s")}";
                m_ClientChainRow.style.display = DisplayStyle.Flex;
            }
            else
            {
                m_ClientChainRow.style.display = DisplayStyle.None;
            }
        }

        void SetValidationInfo(ValidationDecision decision)
        {
            m_ValidationStatus.text = decision.Status.ToString();

            Color? statusColor = decision.Status == ValidationStatus.Accepted ? Color.green :
                              decision.Status == ValidationStatus.Warning ? new Color(1f, 1f, 0f) :
                              decision.Status == ValidationStatus.Rejected ? Color.red : null;

            if (statusColor.HasValue)
            {
                SetLabelColor(m_ValidationStatus, statusColor.Value);
            }

            m_ValidationReason.text = decision.Reason;
        }

        void SetLabelColor(Label label, Color color)
        {
            label.style.color = color;
        }

        /// <summary>
        /// Set whether the details foldout is expanded by default.
        /// </summary>
        public void SetDetailsFoldoutExpanded(bool expanded)
        {
            if (m_DetailsFoldout != null)
            {
                m_DetailsFoldout.value = expanded;
            }
        }
    }
}
