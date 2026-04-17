using System;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Settings.Utilities
{
    /// <summary>
    /// Unified save manager that handles both debounced and periodic saves.
    /// Eliminates the need for duplicate save management code across classes.
    /// </summary>
    class SaveManager
    {
        readonly Action m_SaveAction;

        volatile bool m_IsDirty;
        CancellationTokenSource m_SaveTokenSource;

        public SaveManager(Action saveAction)
        {
            m_SaveAction = saveAction;
            Register();
        }

        public void MarkDirty()
        {
            m_IsDirty = true;
        }

        public void SaveImmediately()
        {
            if (m_IsDirty)
            {
                m_SaveTokenSource?.Cancel();
                SaveIfDirty();
            }
        }

        void Register()
        {
            EditorApplication.quitting += OnEditorQuitting;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        public void Unregister()
        {
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
            m_SaveAction();
        }

        void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) => SaveIfDirty();
        void OnEditorQuitting() => SaveImmediately();
    }
}
