using System;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Editor.Config.Credentials;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor.Config
{
    class AssistantConfiguration
    {
        public IAssistantBackend Backend { get; internal set; }
        public ToolInteractionAndPermissionBridge Bridge { get; internal set; }

        public ICredentialsProvider CredentialsProvider { get; }

        public AssistantConfiguration(
            IAssistantBackend backend = null,
            ToolInteractionAndPermissionBridge bridge = null,
            ICredentialsProvider credentialsProvider = null)
        {
            Backend = backend;
            Bridge = bridge;
            CredentialsProvider = credentialsProvider;
        }
    }
}
