using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Becool.UnityMcpLens.Editor.Settings;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    [InitializeOnLoad]
    static class EditorHealthPublisher
    {
        const int HealthSchemaVersion = 1;
        const double HealthWriteIntervalSeconds = 5d;
        static readonly int s_EditorPid = Process.GetCurrentProcess().Id;
        static readonly DateTime s_ProcessStartUtc = GetProcessStartUtc();
        static string s_LastHealthJson;
        static DateTime s_LastWriteUtc;
        static double s_NextWriteAt;
        static string s_LifecycleState = "active";

        static EditorHealthPublisher()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
            Publish(force: true);
        }

        static void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < s_NextWriteAt)
                return;

            s_LifecycleState = "active";
            Publish(force: false);
            s_NextWriteAt = now + HealthWriteIntervalSeconds;
        }

        static void OnBeforeAssemblyReload()
        {
            s_LifecycleState = "assembly_reload_starting";
            Publish(force: true);
        }

        [DidReloadScripts]
        static void OnAfterScriptsReloaded()
        {
            s_LifecycleState = "assembly_reload_restarted";
            Publish(force: true);
            s_LifecycleState = "active";
            s_NextWriteAt = 0;
        }

        static void OnEditorQuitting()
        {
            s_LifecycleState = "quitting";
            Publish(force: true);
        }

        static void Publish(bool force)
        {
            try
            {
                string statusDirectory = MCPConstants.StatusDirectory;
                Directory.CreateDirectory(statusDirectory);

                string json = JsonConvert.SerializeObject(Capture(), Formatting.Indented);
                if (!force &&
                    string.Equals(json, s_LastHealthJson, StringComparison.Ordinal) &&
                    (DateTime.UtcNow - s_LastWriteUtc).TotalSeconds < HealthWriteIntervalSeconds)
                {
                    return;
                }

                AtomicWrite(GetHealthFilePath(statusDirectory), json);
                s_LastHealthJson = json;
                s_LastWriteUtc = DateTime.UtcNow;
            }
            catch
            {
                // Health publishing is diagnostic only and must never destabilize the editor.
            }
        }

        static object Capture()
        {
            DateTime nowUtc = DateTime.UtcNow;
            string captureError = null;
            string activeSceneName = string.Empty;
            string activeScenePath = string.Empty;

            try
            {
                Scene activeScene = SceneManager.GetActiveScene();
                if (activeScene.IsValid())
                {
                    activeSceneName = activeScene.name;
                    activeScenePath = activeScene.path;
                }
            }
            catch (Exception ex)
            {
                captureError = ex.GetType().Name + ": " + ex.Message;
            }

            bool isCompiling = ReadBool(() => EditorApplication.isCompiling, ref captureError);
            bool isUpdating = ReadBool(() => EditorApplication.isUpdating, ref captureError);
            bool isPlaying = ReadBool(() => EditorApplication.isPlaying, ref captureError);
            bool isPaused = ReadBool(() => EditorApplication.isPaused, ref captureError);
            bool isPlayingOrWillChange = ReadBool(() => EditorApplication.isPlayingOrWillChangePlaymode, ref captureError);
            bool isBuildingPlayer = ReadBool(() => BuildPipeline.isBuildingPlayer, ref captureError);

            return new
            {
                health_schema_version = HealthSchemaVersion,
                editor_heartbeat_utc = nowUtc.ToString("O"),
                state_captured_utc = nowUtc.ToString("O"),
                editor_pid = s_EditorPid,
                editor_process_start_utc = s_ProcessStartUtc.ToString("O"),
                project_path = Application.dataPath,
                project_root = GetProjectRoot(),
                unity_version = Application.unityVersion,
                lifecycle_state = s_LifecycleState,
                is_compiling = isCompiling,
                is_importing = isUpdating,
                is_updating = isUpdating,
                is_playing = isPlaying,
                is_paused = isPaused,
                is_playing_or_will_change_playmode = isPlayingOrWillChange,
                is_building_player = isBuildingPlayer,
                active_scene_name = activeSceneName,
                active_scene_path = activeScenePath,
                capture_error = captureError
            };
        }

        static bool ReadBool(Func<bool> getter, ref string captureError)
        {
            try
            {
                return getter();
            }
            catch (Exception ex)
            {
                if (string.IsNullOrWhiteSpace(captureError))
                    captureError = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        static void AtomicWrite(string path, string json)
        {
            string tempPath = path + "." + s_EditorPid + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, null);
                    return;
                }
                catch
                {
                    File.Delete(path);
                }
            }

            File.Move(tempPath, path);
        }

        static string GetHealthFilePath(string statusDirectory)
        {
            return Path.Combine(statusDirectory, $"editor-health-{ComputeProjectHash(Application.dataPath)}-{s_EditorPid}.json");
        }

        static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        static string ComputeProjectHash(string input)
        {
            try
            {
                using (SHA1 sha1 = SHA1.Create())
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
                    byte[] hashBytes = sha1.ComputeHash(bytes);
                    var sb = new StringBuilder();
                    foreach (byte b in hashBytes)
                    {
                        sb.Append(b.ToString("x2"));
                    }
                    return sb.ToString().Substring(0, 8);
                }
            }
            catch
            {
                return "default";
            }
        }

        static DateTime GetProcessStartUtc()
        {
            try
            {
                return Process.GetCurrentProcess().StartTime.ToUniversalTime();
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }
    }
}
