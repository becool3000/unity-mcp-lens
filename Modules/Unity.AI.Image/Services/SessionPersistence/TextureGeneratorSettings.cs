using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Image.Services.Stores.States;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Image.Services.SessionPersistence
{
    [FilePath("UserSettings/AI.Image/TextureGeneratorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    class TextureGeneratorSettings : ScriptableSingleton<TextureGeneratorSettings>
    {
        [SerializeField]
        Session m_Session = new();

        public Session session
        {
            get => m_Session;
            set
            {
                m_Session = value;
                MarkDirty();
            }
        }

        bool m_IsDirty;
        CancellationTokenSource m_SaveTokenSource;

        public void MarkDirty()
        {
            m_IsDirty = true;
            _ = DebounceSettingsSave();
        }

        public async Task DebounceSettingsSave(int delayMilliseconds = 250)
        {
            var oldTokenSource = m_SaveTokenSource;
            m_SaveTokenSource = new CancellationTokenSource();

            await GeneratorSettingsUtility.DebounceSettingsSave(oldTokenSource, SaveSettings,
                ex => Debug.LogWarning($"Error during texture generator settings save: {ex}"),
                delayMilliseconds);
        }

        public void SaveSettings()
        {
            if (!m_IsDirty)
                return;

            m_IsDirty = false;
            Save(true);
        }

        void OnEnable() => EditorApplication.quitting += OnEditorQuitting;

        void OnDisable()
        {
            EditorApplication.quitting -= OnEditorQuitting;

            // Cancel any pending save
            if (m_SaveTokenSource != null)
            {
                m_SaveTokenSource.Cancel();
                m_SaveTokenSource.Dispose();
                m_SaveTokenSource = null;
            }

            if (m_IsDirty)
                SaveSettings();
        }

        void OnEditorQuitting() => SaveSettings();
    }
}
