using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace Unity.AI.Assistant.Tools.Editor
{
    static class PackageTools
    {
        const string k_GetPackageDataFunctionId = "Unity.PackageManager.GetData";
        const string k_PackageManagerActionFunctionId = "Unity.PackageManager.ExecuteAction";

        public enum Operation
        {
            Add,
            Remove,
            Embed,
            Unembed,
            Sample
        }

        [Serializable]
        public class SampleInfo
        {
            public string Name;
            public string Description;
            public bool Imported;
        }

        [Serializable]
        public class PackageInfo
        {
            public string Name;
            public string Description;
            public string Version;
            public bool Embedded;
            public List<SampleInfo> Samples;

            public static PackageInfo FromPackageInfo(UnityEditor.PackageManager.PackageInfo packageInfo)
            {
                var samples = Sample.FindByPackage(packageInfo.name, packageInfo.version).Select(s => new SampleInfo() { Name = s.displayName, Description = s.description, Imported = s.isImported });
                return new() { Name = packageInfo.name, Description = packageInfo.description, Version = packageInfo.version, Samples = samples.ToList(), Embedded = IsEmbeddedPackage(packageInfo)};
            }
        }

        [Serializable]
        public class DownloadablePackageInfo
        {
            public string Name;
            public string Description;
            public List<string> Versions;

            public static DownloadablePackageInfo FromPackageInfo(UnityEditor.PackageManager.PackageInfo packageInfo)
            {
                return new() { Name = packageInfo.name, Description = packageInfo.description, Versions = packageInfo.versions.all.ToList() };
            }
        }

        [Serializable]
        public class GetPackageDataOutput
        {
            public List<PackageInfo> InstalledPackages = new();
            public List<DownloadablePackageInfo> AvailablePackages = new();
        }

        [AgentTool(
            "Get the description of the given unity package. This cannot be used to install packages, only retrieve information about them.",
            k_GetPackageDataFunctionId,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        internal static async Task<GetPackageDataOutput> GetPackageData(
            [Parameter("Optional - The identifier of the package, e.g: com.unity.modules.physics. Leave blank (empty string) to get all packages.")]
            string packageID,
            [Parameter("Specify to only retrieve information about installed packages.")]
            bool installedOnly)
        {
            var output = new GetPackageDataOutput();

            // Get list of installed packages
            var listRequest = Client.List(true, true);

            await listRequest.WaitForCompletion();

            if (listRequest.Status != StatusCode.Success)
            {
                throw new Exception(listRequest.Error.message);
            }

            // Add all packages to output
            if (string.IsNullOrEmpty(packageID))
            {
                foreach (var currentPackage in listRequest.Result)
                {
                    output.InstalledPackages.Add(PackageInfo.FromPackageInfo(currentPackage));
                }
            }
            else
            {
                foreach (var currentPackage in listRequest.Result)
                {
                    if (currentPackage.packageId.Contains(packageID, StringComparison.InvariantCultureIgnoreCase))
                        output.InstalledPackages.Add(PackageInfo.FromPackageInfo(currentPackage));
                }
            }

            // Get the entire manifest if neccessary
            if (!installedOnly)
            {
                var searchRequest = Client.SearchAll();

                await searchRequest.WaitForCompletion();

                if (searchRequest.Status != StatusCode.Success)
                {
                    throw new Exception(searchRequest.Error.message);
                }

                foreach (var currentPackage in searchRequest.Result)
                {
                    output.AvailablePackages.Add(DownloadablePackageInfo.FromPackageInfo(currentPackage));
                }
            }

            return output;
        }

        [AgentTool(
            "Add or Remove packages and install their samples.",
            k_PackageManagerActionFunctionId,
            tags: FunctionCallingUtilities.k_StaticContextTag,
            Mcp = McpAvailability.Available)]
        internal static async Task<string> PackageManagerExecuteAction(
            ToolExecutionContext context,
            [Parameter(@"The operation to perform: 'Add', 'Remove', 'Embed', 'Unembed', 'Sample'.
                        All operations besides 'Add' require the package to already be installed.
                        'Embed' makes the package writeable/modifiable by the user and embed the code in project.
                        'Unembed' deletes an embedded package from the project.
                        'Sample' will install/reinstall a specified package sample")]
            Operation operation,
            [Parameter("The identifier of the package, e.g: com.unity.modules.physics.")]
            string package,
            [Parameter("Optional - The version of the package to add")]
            string version,
            [Parameter("Optional - The sample to install - only used by the 'Sample' operation.")]
            int sampleIndex = -1)
        {
            if (string.IsNullOrWhiteSpace(package))
            {
                throw new Exception("Package identifier cannot be empty.");
            }

            Request clientRequest = null;
            // Every operation potentially causes a recompile
            // We pause that for now and then restart it next frame
            EditorApplication.LockReloadAssemblies();
            MainThread.DispatchAndForget(EditorApplication.UnlockReloadAssemblies);

            switch (operation)
            {
                case Operation.Add:
                    if (!string.IsNullOrWhiteSpace(version))
                        package = $"{package}@{version}";

                    clientRequest = Client.Add(package);

                    break;
                case Operation.Remove:
                    clientRequest = Client.Remove(package);

                    break;
                case Operation.Embed:
                {
                    var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(package);
                    if (packageInfo == null)
                    {
                        throw new Exception($"Package embed: {package} failed. Package is not installed as a direct dependency.  Please use the 'add' command first.");
                    }
                    if (IsEmbeddedPackage(packageInfo))
                    {
                        return $"Package embed: {package} already embedded. No action needed";
                    }
                    clientRequest = Client.Embed(package);
                }
                    break;
                case Operation.Unembed:
                {
                    var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(package);
                    if (packageInfo == null)
                    {
                        throw new Exception($"Package unembed: {package} not installed. No action needed");
                    }
                    var path = packageInfo.assetPath;

                    if (IsEmbeddedPackage(packageInfo))
                    {
                        // Can't use the delete tool here - deleting embedded package folders via assetdatabase behaves eratically
                        var rootPath = Path.GetDirectoryName(Application.dataPath);
                        if (string.IsNullOrWhiteSpace(rootPath))
                        {
                            throw new Exception($"Package unembed: {package} failed: Delete path unavailable. Please remove the package folder manually.");
                        }
                        var fullPath = Path.GetFullPath(Path.Combine(rootPath, path));

                        // GetFullPath will have resolved any sort of relative path shenanigans
                        // We double check this path one more time to make sure we're in the packages folder
                        var packagesPath = Path.Combine(rootPath, "Packages");

                        if (!fullPath.StartsWith(packagesPath, StringComparison.InvariantCultureIgnoreCase))
                        {
                            throw new Exception($"Package unembed: {package} failed: Delete path outside of packages folder: {fullPath}");
                        }
                        await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Delete, fullPath);
                        if (!FileUtil.DeleteFileOrDirectory(fullPath))
                        {
                            throw new Exception($"Package unembed: {package} failed.");
                        }

                        return $"Package unembed: {package} complete.";
                    }
                    else
                    {
                        return $"Package unembed: {package} is not embedded. No action needed.";
                    }
                }
                case Operation.Sample:
                {
                    var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(package);
                    if (packageInfo == null)
                    {
                        throw new Exception($"Sample install failed! {package} not installed");
                    }
                    var samples = Sample.FindByPackage(packageInfo.name, packageInfo.version).ToList();

                    if (sampleIndex < 0 || sampleIndex >= samples.Count())
                    {
                        throw new Exception($"Sample install failed! SampleId {sampleIndex} is out of range.");
                    }
                    var sample = samples[sampleIndex];
                    if (!sample.isImported)
                    {
                        if(!sample.Import(Sample.ImportOptions.HideImportWindow | Sample.ImportOptions.OverridePreviousImports))
                        {
                            throw new Exception($"Failed to import {package} sample {sampleIndex}");
                        }
                        else
                        {
                            return $"Installed {package} sample {sampleIndex}";
                        }
                    }
                    else
                    {
                        return $"{package} sample {sampleIndex} already installed.";
                    }
                }
            }

            var message = "";
            if (clientRequest != null)
            {
                await clientRequest.WaitForCompletion();
                if (clientRequest.Status != StatusCode.Success)
                {
                    throw new Exception(clientRequest.Error.message);
                }
                else
                {
                    message = $"{operation} : {package} successful!";
                }
            }
            return message;
        }


        static internal bool IsEmbeddedPackage(UnityEditor.PackageManager.PackageInfo packageInfo)
        {
            return packageInfo.source == PackageSource.Embedded;
        }
    }
}
