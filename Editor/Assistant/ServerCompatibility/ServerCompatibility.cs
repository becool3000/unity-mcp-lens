using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.Ai.Assistant.Protocol.Model;
using Unity.AI.Assistant.Socket.ErrorHandling;
using Unity.AI.Toolkit.Accounts.Services;
using VersionSupportInfo = Unity.AI.Assistant.ApplicationModels.VersionSupportInfo;

namespace Unity.AI.Assistant.Editor.ServerCompatibility
{
    static class ServerCompatibility
    {
        // Hard code the version this client is compatible with
        const string k_Version = "v1";

        // Assume the server is compatible to being with
        public static CompatibilityStatus Status => UserSessionState.instance.CompatibilityStatus;

        public static Action<CompatibilityStatus> OnCompatibilityChanged;
        static IAssistantBackend s_AssistantBackend;

        static Task m_RefreshTask;

        static ServerCompatibility()
        {
            Account.sessionStatus.OnChange -= Refresh;
            Account.sessionStatus.OnChange += Refresh;
            Account.settings.OnChange -= Refresh;
            Account.settings.OnChange += Refresh;
        }

        public static void SetBackend(IAssistantBackend backend)
        {
            s_AssistantBackend = backend;

            Refresh();
        }

        /// <summary>
        /// Binds the event handler to the compatibility changed event. Immediately invokes the event handler with
        /// the current compatibility status.
        /// </summary>
        /// <param name="eventHandler"></param>
        /// <returns></returns>
        public static void Bind(Action<CompatibilityStatus> eventHandler)
        {
            eventHandler?.Invoke(Status);
            OnCompatibilityChanged += eventHandler;
            Refresh();
        }

        public static void Unbind(Action<CompatibilityStatus> eventHandler) => OnCompatibilityChanged -= eventHandler;

        /// <summary>
        /// Check the server compatibility status.
        /// </summary>
        public static void Refresh() => m_RefreshTask ??= UpdateCompatibility();

        static async Task UpdateCompatibility()
        {
            try
            {
                if(s_AssistantBackend == null)
                    return;

                // Try to see if there is some cached info about this.
                if (UserSessionState.instance.CompatibilityStatus != CompatibilityStatus.Undetermined)
                    return;

                // At this point, a cache was not used. A webrequest needs to be made and the resulting status cached
                var result = await s_AssistantBackend.GetVersionSupportInfo(CredentialsContext.Default());

                // On failure, simply ignore. We don't want to spam notifications because of internet connect issues.
                if (result.Status != BackendResult.ResultStatus.Success)
                {
                    ErrorHandlingUtility.InternalLogBackendResult(result);
                    UserSessionState.instance.CompatibilityStatus = CompatibilityStatus.Unknown;
                    OnCompatibilityChanged?.Invoke(Status);
                    return;
                }

                var versionInfo =result.Value;
                if (versionInfo != null)
                {
                    var relevantVersion = versionInfo
                        .FirstOrDefault(v => v.RoutePrefix == k_Version);

                    if(relevantVersion == default)
                        UserSessionState.instance.CompatibilityStatus = CompatibilityStatus.Unsupported;
                    else
                        UserSessionState.instance.CompatibilityStatus = GetCompatibilityStatus(relevantVersion.SupportStatus);

                    OnCompatibilityChanged?.Invoke(Status);
                }
                else
                {
                    UserSessionState.instance.CompatibilityStatus = CompatibilityStatus.Unknown;
                    OnCompatibilityChanged?.Invoke(Status);
                }
            }
            catch (Exception)
            {
                UserSessionState.instance.CompatibilityStatus = CompatibilityStatus.Unknown;
                OnCompatibilityChanged?.Invoke(Status);
            }

            m_RefreshTask = null;
        }

        static CompatibilityStatus GetCompatibilityStatus(ApplicationModels.VersionSupportInfo.SupportStatusEnum status)
        {
            return status switch
            {
                VersionSupportInfo.SupportStatusEnum.Supported => CompatibilityStatus.Supported,
                VersionSupportInfo.SupportStatusEnum.Deprecated => CompatibilityStatus.Deprecated,
                VersionSupportInfo.SupportStatusEnum.Unsupported => CompatibilityStatus.Unsupported,
                _ => CompatibilityStatus.Unsupported
            };
        }

        public enum CompatibilityStatus
        {
            /// <summary>
            /// The compatibility status has not yet been determined. A web request needs to be sent to the server to
            /// determine the status.
            /// </summary>
            Undetermined,

            /// <summary>
            /// The server has reported that this client is supported.
            /// </summary>
            Supported,

            /// <summary>
            /// The server has reported that this client is deprecated.
            /// </summary>
            Deprecated,

            /// <summary>
            /// The server has reported that this client is unsupported.
            /// </summary>
            Unsupported,

            /// <summary>
            /// Compatibility status is unknown when the server is not reachable, therefore the server cannot report
            /// its status. This should not be used to block the user and other mechanisms should be used to handle
            /// detecting network issues.
            /// </summary>
            Unknown
        }
    }
}
