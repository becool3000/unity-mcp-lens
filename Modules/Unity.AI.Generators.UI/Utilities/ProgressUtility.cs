using System;
using System.Collections.Generic;
using UnityEditor;

namespace Unity.AI.Generators.UI.Utilities
{
    [InitializeOnLoad]
    static class ProgressUtility
    {
        static readonly HashSet<int> k_ProgressTasks = new();

        static ProgressUtility()
        {
            try
            {
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            }
            catch
            {
                // This is a low priority feature, it should never throw or log to the console regardless of what happens.
            }
        }

        static void OnBeforeAssemblyReload()
        {
            try
            {
                foreach (var taskId in k_ProgressTasks)
                {
                    if (Progress.Exists(taskId))
                    {
                        Progress.Finish(taskId);
                    }
                }
                k_ProgressTasks.Clear();
            }
            catch
            {
                // This is a low priority feature, it should never throw or log to the console regardless of what happens.
                // It should not interrupt important code.
            }
        }

        public static int Start(string name, string description = null, Progress.Options options = Progress.Options.None)
        {
            var taskId = 0;
            try
            {
                taskId = Progress.Start(name, description, options);
                k_ProgressTasks.Add(taskId);
            }
            catch
            {
                // ignore
            }
            return taskId;
        }

        public static void Finish(int taskId, Progress.Status status = Progress.Status.Succeeded)
        {
            try
            {
                k_ProgressTasks.Remove(taskId);
                if (Progress.Exists(taskId))
                {
                    Progress.Finish(taskId, status);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
