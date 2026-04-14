using System;
using System.Text;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;
using UnityEditorInternal;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class ProfilingSessionTools
    {
        public const string InitializeToolId = "Unity.Profiler.Initialize";
        
        // Note: Has to be a syntax-valid relative path
        const string k_ActiveSessionPath = ".active";

        [AgentTool(
            "Initializes a profiling session so that its data is available and return information about the session. " +
            "You should use this tool if you don't have access to a specific profiling session already.",
            id: InitializeToolId,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask)]
        public static async Task<string> InitializeSession(
            ToolExecutionContext context,
            [Parameter("Optional: specify directly the path of the profiling session to load, if known. Leave empty otherwise.")]
            string sessionPath = null
        )
        {
            if (string.IsNullOrEmpty(sessionPath) && !ProfilerUtils.HasInMemorySession())
            {
                try
                {
                    await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
                }
                catch (InvalidOperationException)
                {
                    // Fall back to the existing capture-selection flow when no active in-memory session can be bootstrapped.
                }
            }

            // If a profiler window is already open with an in-memory session, keep using it
            if (EditorWindow.HasOpenInstances<ProfilerWindow>() && ProfilerUtils.HasInMemorySession())
            {
                // TODO: Check that the sessionPath is equivalent to the in-memory session
                return "Using opened profiling session.";
            }

            var profilingSessions = await SessionProvider.GetProfilingSessions(context);

            // Add an extra entry for the active session
            if (ProfilerUtils.HasInMemorySession())
            {
                var inMemorySession = new SessionProvider.ProfilerSessionInfo();
                inMemorySession.ProjectRelativePath = k_ActiveSessionPath;
                inMemorySession.FileName = "Active Session";
                profilingSessions.Insert(0, inMemorySession);
            }

            SessionProvider.ProfilerSessionInfo selectedSession = null;

            // No specific session provided: automatically pick or ask user
            if (string.IsNullOrEmpty(sessionPath))
            {
                // No profiling session found
                if (profilingSessions.Count == 0)
                {
                    // Only push interaction if not in auto-run mode
                    if (!AssistantEditorPreferences.AutoRun)
                    {
                        var recordSessionInteraction = new RecordSessionInteraction();
                        await context.Interactions.WaitForUser(recordSessionInteraction);
                    }

                    // Signal LLM as we cannot wait for the user to actually record a session
                    throw new Exception("No profiling sessions found. Need to wait for user to record one.");
                }

                // By default, pick the first (most recent or in-memory)
                selectedSession = profilingSessions[0];

                // If more than a single session, let the user pick one
                if (profilingSessions.Count != 1)
                {
                    // In auto-run mode, let the LLM decide
                    if (AssistantEditorPreferences.AutoRun)
                    {
                        var sb = new StringBuilder();
                        sb.Append("Available profiling sessions:\n");
                        foreach (var profilingSession in profilingSessions)
                        {
                            if (profilingSession.ProjectRelativePath == k_ActiveSessionPath)
                            {
                                sb.AppendFormat($" - {profilingSession.ProjectRelativePath} (last loaded or captured session)\n");
                            }
                            else
                            {
                                sb.AppendFormat($" - {profilingSession.ProjectRelativePath}\n");
                            }
                        }

                        sb.Append("Call this tool again with the path of the profiling session you want to load.");
                        return sb.ToString();
                    }

                    // Otherwise, ask the user to select the session
                    var pickSessionInteraction = new PickSessionInteraction(profilingSessions);
                    selectedSession = await context.Interactions.WaitForUser(pickSessionInteraction);
                }
            }
            // Session path provided: load it if available
            else
            {
                // Identify the session in the available list
                foreach (var profilingSession in profilingSessions)
                {
                    if (PathUtils.PathsEqual(profilingSession.ProjectRelativePath, sessionPath))
                    {
                        selectedSession = profilingSession;
                        break;
                    }
                }

                // If no full path match then try match by a filename
                // Identify the session in the available list
                foreach (var profilingSession in profilingSessions)
                {
                    if (profilingSession.FileName == sessionPath)
                    {
                        selectedSession = profilingSession;
                        break;
                    }
                }
                
                // Session could not be found
                if (selectedSession == null)
                    throw new Exception("Could not find the profiling session at the given path.");
            }

            // Skip loading if using active session
            if (selectedSession.ProjectRelativePath == k_ActiveSessionPath)
                return "Loaded in-memory profiling sessions.";

            // Cleanup the cache related to the current session if we load another capture
            context.Conversation.ClearFrameDataCache();

            // Load profiling session
            ProfilerDriver.LoadProfile(selectedSession.ProjectRelativePath, false);
            return $"Initialized session at path: {selectedSession.ProjectRelativePath}";
        }
    }
}
