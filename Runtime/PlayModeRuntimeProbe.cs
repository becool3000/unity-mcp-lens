using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Runtime
{
    [Serializable]
    public sealed class PlayModeRuntimeProbeSnapshot
    {
        public bool IsAvailable { get; set; }

        public bool HasAdvancedFrames { get; set; }

        public bool RunInBackground { get; set; }

        public bool IsFocused { get; set; }

        public int UpdateCount { get; set; }

        public int FixedUpdateCount { get; set; }

        public int FrameCount { get; set; }

        public float RuntimeTime { get; set; }

        public float UnscaledTime { get; set; }

        public float FixedTime { get; set; }

        public double LastRealtimeSinceStartup { get; set; }

        public string ActiveSceneName { get; set; } = string.Empty;
    }

    public sealed class PlayModeRuntimeProbe : MonoBehaviour
    {
        private static PlayModeRuntimeProbe instance;
        private static PlayModeRuntimeProbeSnapshot snapshot = new();
        private static bool previousRunInBackground;
        private int updateCount;
        private int fixedUpdateCount;

        public static bool TryGetSnapshot(out PlayModeRuntimeProbeSnapshot currentSnapshot)
        {
            currentSnapshot = snapshot;
            return currentSnapshot != null && currentSnapshot.IsAvailable;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (!Application.isPlaying || instance != null)
            {
                return;
            }

            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;

            GameObject root = new("UnityMcpPlayModeRuntimeProbe");
            root.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(root);
            instance = root.AddComponent<PlayModeRuntimeProbe>();
            snapshot = new PlayModeRuntimeProbeSnapshot
            {
                IsAvailable = true,
                ActiveSceneName = SceneManager.GetActiveScene().name,
            };
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            Application.runInBackground = true;
            hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);
            CaptureSnapshot();
        }

        private void Update()
        {
            updateCount++;
            CaptureSnapshot();
        }

        private void FixedUpdate()
        {
            fixedUpdateCount++;
            CaptureSnapshot();
        }

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            Application.runInBackground = previousRunInBackground;
            instance = null;
            snapshot = new PlayModeRuntimeProbeSnapshot();
        }

        private void CaptureSnapshot()
        {
            snapshot = new PlayModeRuntimeProbeSnapshot
            {
                IsAvailable = true,
                HasAdvancedFrames = updateCount > 1 || fixedUpdateCount > 0 || Time.time > 0.05f,
                RunInBackground = Application.runInBackground,
                IsFocused = Application.isFocused,
                UpdateCount = updateCount,
                FixedUpdateCount = fixedUpdateCount,
                FrameCount = Time.frameCount,
                RuntimeTime = Time.time,
                UnscaledTime = Time.unscaledTime,
                FixedTime = Time.fixedTime,
                LastRealtimeSinceStartup = Time.realtimeSinceStartupAsDouble,
                ActiveSceneName = SceneManager.GetActiveScene().name,
            };
        }
    }
}
