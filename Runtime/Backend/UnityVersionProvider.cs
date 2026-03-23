using System.Collections.Generic;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Backend
{
    class UnityVersionProvider : IUnityVersionProvider
    {
        static readonly string[] k_UnityVersionField;

        static UnityVersionProvider()
        {
            k_UnityVersionField = new[]
            {
                ProjectVersionUtils.GetProjectVersion(ProjectVersionUtils.VersionDetail.Revision)
            };
        }

        public IReadOnlyList<string> Version => k_UnityVersionField;
    }
}
