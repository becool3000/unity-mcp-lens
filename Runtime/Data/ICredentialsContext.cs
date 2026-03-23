using System.Collections.Generic;

namespace Unity.AI.Assistant.Data
{
    /// <summary>
    /// Interface for providing credentials and context information that can be accessed from background threads.
    /// Runtime interface implemented by Editor-specific CredentialsContext.
    /// </summary>
    interface ICredentialsContext
    {
        string AccessToken { get; }
        string OrganizationId { get; }
        string ProjectId { get; }
        string AnalyticsSessionId { get; }
        int AnalyticsSessionCount { get; }
        string AnalyticsUserId { get; }
        string EditorVersion { get; }
        string PackageVersion { get; }
        string ApiVersion { get; }

        Dictionary<string, string> Headers { get; }
    }
}
