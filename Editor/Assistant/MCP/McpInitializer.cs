using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Editor.Mcp.Manager;
using Unity.AI.Assistant.Editor.Service;
using Unity.AI.Assistant.Utils;
using UnityEditor;

namespace Unity.AI.Assistant.Editor.Mcp
{
    /// <summary>
    /// Handles MCP initialization when Unity starts
    /// </summary>
    static class McpInitializer
    {
        const int k_MaxRetrySeconds = 300; // 5 minutes
        static int m_RetrySeconds = 2;
        static bool m_IsInitializeHookInstalled;
        static bool m_IsInitializationAttemptRunning;
        static double m_NextInitializationAttemptTime;
        
        [InitializeOnLoadMethod]
        static void InitializeMcpServices()
        {
            if (m_IsInitializeHookInstalled)
                return;

            m_IsInitializeHookInstalled = true;
            EditorApplication.update += TryInitializeMcpServices;
        }

        static async void TryInitializeMcpServices()
        {
            if (m_IsInitializationAttemptRunning)
                return;

            if (EditorApplication.timeSinceStartup < m_NextInitializationAttemptTime)
                return;

            if (!IsEditorStableForInitialization())
                return;

            m_IsInitializationAttemptRunning = true;

            try
            {
                await AssistantGlobal.Services.RegisterService(new McpServerManagerService());
                var handle = AssistantGlobal.Services.GetService<McpServerManagerService>();
                await handle.WaitForRegistrationOrFailure();

                if (handle.State == ServiceState.RegisteredAndInitialized)
                {
                    m_RetrySeconds = 2;
                    m_NextInitializationAttemptTime = 0;
                    EditorApplication.update -= TryInitializeMcpServices;
                    m_IsInitializeHookInstalled = false;
                    return;
                }

                ScheduleRetry(handle.FailureReason ??
                    "The MCP server service failed to initialize and did not report a reason.");
            }
            catch (Exception ex)
            {
                ScheduleRetry(ex.Message);
            }
            finally
            {
                m_IsInitializationAttemptRunning = false;
            }
        }

        static bool IsEditorStableForInitialization()
        {
            return !EditorApplication.isCompiling &&
                   !EditorApplication.isUpdating &&
                   !EditorApplication.isPlayingOrWillChangePlaymode &&
                   !BuildPipeline.isBuildingPlayer;
        }

        static void ScheduleRetry(string reason)
        {
            InternalLog.LogError("The MCP server service failed to initialize. This means that the relay server " +
                                 "failed to initialize or the initialization signalling logic is not working " +
                                 "correctly. A reinitialization is being attempted now. Retrying in " +
                                 $"{m_RetrySeconds} seconds. Reason: {reason}");

            m_NextInitializationAttemptTime = EditorApplication.timeSinceStartup + m_RetrySeconds;
            m_RetrySeconds = Math.Min(m_RetrySeconds * 2, k_MaxRetrySeconds);

            if (m_IsInitializeHookInstalled)
                return;

            m_IsInitializeHookInstalled = true;
            EditorApplication.update += TryInitializeMcpServices;
        }
    }
}
