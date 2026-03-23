using System;
using System.Threading;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Unity.AI.Search.Editor.Utilities
{
    /// <summary>
    /// Unified save manager that handles periodic saves.
    /// Eliminates the need for duplicate save management code across classes.
    /// </summary>
    class PeriodicSaveManager
    {
        readonly Action m_SaveAction;
        readonly float m_IntervalSeconds;
        readonly string m_LogPrefix;

        float m_LastSaveTime;
        bool m_IsDirty;
        CancellationTokenSource m_SaveTokenSource;

        public PeriodicSaveManager(Action saveAction, float intervalSeconds = 300f, string logPrefix = "SmartSave")
        {
            m_SaveAction = saveAction;
            m_IntervalSeconds = intervalSeconds;
            m_LogPrefix = logPrefix;
            m_LastSaveTime = (float)EditorApplication.timeSinceStartup;
            Register();
        }

        public void MarkDirty()
        {
            m_IsDirty = true;
        }

        void SaveImmediately()
        {
            if (m_IsDirty)
            {
                m_SaveTokenSource?.Cancel();
                SaveIfDirty();
                InternalLog.Log($"[{m_LogPrefix}] Force save completed", LogFilter.Search);
            }
        }

        void Register()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.quitting += OnEditorQuitting;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        public void Unregister()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorSceneManager.sceneSaving -= OnSceneSaving;

            m_SaveTokenSource?.Cancel();
            m_SaveTokenSource?.Dispose();
            m_SaveTokenSource = null;
        }

        void SaveIfDirty()
        {
            if (!m_IsDirty) return;

            m_IsDirty = false;
            m_LastSaveTime = (float)EditorApplication.timeSinceStartup;
            m_SaveAction();
        }

        void OnEditorUpdate()
        {
            if (!m_IsDirty) return;

            var currentTime = (float)EditorApplication.timeSinceStartup;
            if (currentTime - m_LastSaveTime >= m_IntervalSeconds)
                SaveIfDirty();
        }

        void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => SaveIfDirty();
        void OnEditorQuitting() => SaveImmediately();
    }
}