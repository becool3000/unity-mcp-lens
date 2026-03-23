using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    static class SessionProvider
    {
        [Serializable]
        public class ProfilerSessionInfo
        {
            public string ProjectRelativePath { get; set; }
            public string FileName { get; set; }
            public long FileSizeBytes { get; set; }
            public DateTime LastModified { get; set; }
        }

        static string ProjectPath => Directory.GetParent(Application.dataPath)?.FullName;
        
        const int k_MaxSessionResultsCount = 5;

        public static async Task<List<ProfilerSessionInfo>> GetProfilingSessions(ToolExecutionContext context)
        {
            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Read, ProjectPath);

            var sessions = new List<ProfilerSessionInfo>();
            var dataFiles = Directory.GetFiles(ProjectPath, "*.data", SearchOption.AllDirectories);

            foreach (var filePath in dataFiles)
            {
                var fileInfo = new FileInfo(filePath);
                var relativePath = Path.GetRelativePath(ProjectPath, filePath);

                sessions.Add(new ProfilerSessionInfo
                {
                    ProjectRelativePath = relativePath.Replace("\\", "/"),
                    FileName = Path.GetFileNameWithoutExtension(fileInfo.Name),
                    FileSizeBytes = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime
                });
            }

            return sessions
                .OrderByDescending(s => s.LastModified)
                .Take(k_MaxSessionResultsCount)
                .ToList();
        }
    }
}
