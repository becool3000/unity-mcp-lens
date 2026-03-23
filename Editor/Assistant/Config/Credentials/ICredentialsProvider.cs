using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.Editor.Config.Credentials
{
    /// <summary>
    /// Provides an implementation for getting credentials. Normally credentials should be pulled from the editor, but
    /// in circumstances like batch mode, credentials are not always available.
    /// </summary>
    interface ICredentialsProvider
    {
        Task<CredentialsContext> GetCredentialsContext(CancellationToken ct = default);
    }
}
