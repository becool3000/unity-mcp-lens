using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Editor.Utils;

namespace Unity.AI.Assistant.Editor.Config.Credentials
{
    class EditorCredentialsProvider : ICredentialsProvider
    {
        public async Task<CredentialsContext> GetCredentialsContext(CancellationToken ct = default)
            => await CredentialsUtils.GetCredentialsContextFromEditor(ct);
    }
}
