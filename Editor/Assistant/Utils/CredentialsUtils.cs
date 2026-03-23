using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Unity.AI.Assistant.Editor.Utils
{
    static class CredentialsUtils
    {
        public static async Task<CredentialsContext> GetCredentialsContextFromEditor(CancellationToken ct = default)
        {
            var orgId = await GetOrganizationIdAsync(ct);

            return new(GetAccessToken(), orgId);
        }

        static async Task<string> GetOrganizationIdAsync(CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested &&
                   string.IsNullOrWhiteSpace(CloudProjectSettings.organizationKey))
            {
                await Task.Yield();
            }

            return CloudProjectSettings.organizationKey;
        }

        static string GetAccessToken()
        {
            return CloudProjectSettings.accessToken;
        }
    }
}
