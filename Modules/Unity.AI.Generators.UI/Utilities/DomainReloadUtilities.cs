using System.IO;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    [InitializeOnLoad]
    static class DomainReloadUtilities
    {
        static readonly string k_TokenFilePath = Path.Combine(TempUtilities.projectRootPath, "Temp", "ai_tk_domain_reload_token");

        static bool? s_WasDomainReloaded;

        public static bool WasDomainReloaded
        {
            get
            {
                if (s_WasDomainReloaded == null)
                {
                    s_WasDomainReloaded = File.Exists(k_TokenFilePath);
                    RemoveToken();
                }
                return s_WasDomainReloaded.Value;
            }
        }

        static DomainReloadUtilities()
        {
            AssemblyReloadEvents.beforeAssemblyReload += CreateToken;

            RemoveTokenAfterOneFrame();

            async void RemoveTokenAfterOneFrame()
            {
                await EditorTask.Yield();
                s_WasDomainReloaded = false;
                RemoveToken();
            }
        }

        static void CreateToken()
        {
            try
            {
                using (var _ = File.Create(k_TokenFilePath)) { }
            }
            catch
            {
                Debug.LogError("Failed to create domain‐reload token.");
            }
        }

        static void RemoveToken()
        {
            try
            {
                if (File.Exists(k_TokenFilePath))
                    File.Delete(k_TokenFilePath);
            }
            catch
            {
                if (File.Exists(k_TokenFilePath))
                    Debug.LogError($"Failed to delete domain‐reload token.");
            }
        }
    }
}
