using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEditor.Compilation;

namespace Unity.AI.Assistant.Editor.Utils
{
    struct CompilationResult
    {
        public bool Success;
        public string ErrorMessage;
    }

    static class ProjectScriptCompilation
    {
        public static Action OnRequestReload;
        public static Action OnBeforeReload;

        static TaskCompletionSource<CompilationResult> s_CompilationTcs;
        static readonly object k_CompilationLock = new ();
        static readonly List<string> s_CompilationErrors = new ();

        static ProjectScriptCompilation()
        {
            AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;

            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        static void HandleBeforeAssemblyReload()
        {
            OnBeforeReload?.Invoke();
        }

        static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            lock (k_CompilationLock)
            {
                foreach (var message in messages)
                {
                    if (message.type == CompilerMessageType.Error)
                    {
                        var errorMessage = $"{message.file}({message.line},{message.column}): error {message.message}";
                        s_CompilationErrors.Add(errorMessage);
                    }
                }
            }
        }

        static void OnCompilationFinished(object sender)
        {
            lock (k_CompilationLock)
            {
                if (s_CompilationTcs != null)
                {

                    var hasErrors = s_CompilationErrors.Count > 0;
                    var errorMessage = hasErrors ? string.Join("\n", s_CompilationErrors) : string.Empty;

                    s_CompilationTcs.TrySetResult(new CompilationResult
                    {
                        Success = !hasErrors,
                        ErrorMessage = errorMessage
                    });

                    s_CompilationTcs = null;
                    s_CompilationErrors.Clear();
                }
            }
        }

        internal static void ForceDomainReload()
        {
            OnRequestReload?.Invoke();

            EditorUtility.RequestScriptReload();
        }

        public static async Task<CompilationResult> RequestProjectCompilation(int timeoutMs = 60000)
        {
            InternalLog.Log("[RequestProjectCompilation]");
            var sw = Stopwatch.StartNew();
            Task<CompilationResult> existingTask = null;
            lock (k_CompilationLock)
            {
                // If there's already a compilation in progress, capture the task
                if (s_CompilationTcs != null)
                {
                    existingTask = s_CompilationTcs.Task;
                }
                else
                {
                    // Clear any previous compilation errors
                    s_CompilationErrors.Clear();
                    s_CompilationTcs = new TaskCompletionSource<CompilationResult>();
                }
            }

            if (existingTask != null)
            {
                InternalLog.Log($"[RequestProjectCompilation] Already existing task, waiting other task.");
                return await existingTask;
            }

            // Request compilation
            var compilationTask = s_CompilationTcs.Task;
            CompilationPipeline.RequestScriptCompilation();

            using var cts = new CancellationTokenSource(timeoutMs);
            var timeoutTask = Task.Delay(timeoutMs, cts.Token);

            var completedTask = await Task.WhenAny(compilationTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                InternalLog.Log($"[RequestProjectCompilation] Timeout (duration: {sw.ElapsedMilliseconds}ms)");
                CompilationResult timeoutResult;
                lock (k_CompilationLock) {
                    timeoutResult = new CompilationResult { Success = false, ErrorMessage = "Compilation timeout: compilation did not complete within the specified time limit." };
                    s_CompilationTcs?.TrySetResult(timeoutResult);
                    s_CompilationTcs = null;
                }
                return timeoutResult;
            }

            InternalLog.Log($"[RequestProjectCompilation] Completed: {(compilationTask.Result.Success ? "success" : "failure")} (duration: {sw.ElapsedMilliseconds}ms)");
            return await compilationTask;
        }
    }
}
