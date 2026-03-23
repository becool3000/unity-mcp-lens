using AiEditorToolsSdk.Domain.Abstractions.Services;

namespace Unity.AI.Generators.Sdk
{
    class PackageInfoProvider : IPackageInfoProvider
    {
        static UnityEditor.PackageManager.PackageInfo s_PackageInfo;
        public string PackageName { get; }
        public string PackageVersion { get; }

        public PackageInfoProvider()
        {
            if (s_PackageInfo == null)
            {
                s_PackageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackageInfoProvider).Assembly);
            }

            if (s_PackageInfo != null)
            {
                PackageName = s_PackageInfo.name;
                PackageVersion = s_PackageInfo.version;
            }
        }
    }
}
