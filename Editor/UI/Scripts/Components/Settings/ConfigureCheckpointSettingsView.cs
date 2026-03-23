using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Checkpoint;
using Unity.AI.Assistant.Editor.Checkpoint.Events;
using Unity.AI.Assistant.Editor.Checkpoint.Git;
using Unity.AI.Assistant.Editor.Utils.Event;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    [UxmlElement]
    partial class ConfigureCheckpointSettingsView : ManagedTemplate
    {
        const string k_ValidationSuccessClass = "checkpoint-validation-success";
        const string k_CustomText = "Custom";
        const string k_LoadingText = "Loading...";

        static bool s_DetectionComplete;
        static string s_EmbeddedVersion;
        static string s_SystemVersion;
        static bool s_SystemFound;
        static string s_LastCustomPath;
        static readonly Dictionary<GitInstanceType, GitValidationResult> k_ValidationCache = new();

        Toggle m_EnableToggle;
        VisualElement m_InitIndicator;
        Label m_InitLabel;
        VisualElement m_DisclaimerContainer;
        LoadingSpinner m_Spinner;

        DropdownField m_GitDropdown;
        VisualElement m_CustomPathContainer;
        TextField m_CustomPathField;
        VisualElement m_ValidationContainer;
        Label m_ValidationLabel;
        VisualElement m_ErrorContainer;
        Label m_ErrorText;

        TextField m_RepoLocationField;
        DropdownField m_RetentionDropdown;

        bool m_IsInitializing;
        bool m_IsLoading;
        string m_ErrorMessage;
        List<GitInstanceType> m_DropdownTypes = new();

        static bool EmbeddedAvailable => !string.IsNullOrEmpty(s_EmbeddedVersion);

        public ConfigureCheckpointSettingsView() : base(AssistantUIConstants.UIModulePath)
        {
            RegisterAttachEvents(OnAttach, OnDetach);
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_EnableToggle = view.Q<Toggle>("checkpointEnabledToggle");
            m_EnableToggle.RegisterValueChangedCallback(OnEnableToggled);

            m_InitIndicator = view.Q<VisualElement>("checkpointInitializingIndicator");
            m_InitLabel = m_InitIndicator.Q<Label>("initializingLabel");
            m_DisclaimerContainer = view.Q<VisualElement>("checkpointDisclaimerContainer");
            m_Spinner = new LoadingSpinner();
            m_InitIndicator.Insert(0, m_Spinner);

            m_GitDropdown = view.Q<DropdownField>("gitInstanceDropdown");
            m_GitDropdown.RegisterValueChangedCallback(OnGitDropdownChanged);

            m_CustomPathContainer = view.Q<VisualElement>("customGitPathContainer");
            m_CustomPathField = view.Q<TextField>("customGitPathField");
            m_CustomPathField.RegisterCallback<FocusOutEvent>(OnCustomPathFocusOut);
            m_CustomPathField.RegisterCallback<KeyDownEvent>(OnCustomPathKeyDown);
            view.SetupButton("browseGitPathButton", OnBrowseGitPath);

            m_ValidationContainer = view.Q<VisualElement>("gitValidationContainer");
            m_ValidationLabel = view.Q<Label>("gitValidationLabel");
            m_ErrorContainer = view.Q<VisualElement>("gitValidationErrorContainer");
            m_ErrorText = view.Q<Label>("gitValidationErrorText");

            m_RepoLocationField = view.Q<TextField>("repositoryLocationField");
            m_RepoLocationField.isReadOnly = true;
            view.SetupButton("openRepositoryButton", OnOpenRepo);

            m_RetentionDropdown = view.Q<DropdownField>("checkpointRetentionWeeksDropdown");
            m_RetentionDropdown.choices = new List<string> { "1", "2" };
            m_RetentionDropdown.SetValueWithoutNotify(AssistantProjectPreferences.CheckpointRetentionWeeks.ToString());
            m_RetentionDropdown.RegisterValueChangedCallback(e =>
            {
                if (int.TryParse(e.newValue, out var weeks))
                {
                    AssistantProjectPreferences.CheckpointRetentionWeeks = weeks;
                }
            });
        }

        void OnAttach(AttachToPanelEvent evt)
        {
            AssistantProjectPreferences.CheckpointEnabledChanged += RefreshUI;
            AssistantProjectPreferences.GitInstanceTypeChanged += OnGitTypeChanged;

            k_ValidationCache.Clear();
            m_ErrorMessage = null;

            if (s_DetectionComplete)
            {
                EnsureValidSelection();
                RefreshUI();
            }
            else
            {
                m_IsLoading = true;
                RefreshUI();
                DetectGitAsync();
            }
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            AssistantProjectPreferences.CheckpointEnabledChanged -= RefreshUI;
            AssistantProjectPreferences.GitInstanceTypeChanged -= OnGitTypeChanged;
        }

        void RefreshUI()
        {
            var isLoading = m_IsLoading;
            var isInitializing = m_IsInitializing;
            var isBusy = isLoading || isInitializing;
            var checkpointEnabled = AssistantProjectPreferences.CheckpointEnabled;
            var gitType = AssistantProjectPreferences.GitInstanceType;
            var validation = isBusy ? default : GetValidation(useCache: true);

            // Loading/initializing indicator
            m_InitIndicator.SetDisplay(isBusy);
            if (isBusy)
            {
                m_Spinner.Show();
                m_InitLabel.text = isLoading ? "Detecting git..." : "Initializing...";
            }
            else
            {
                m_Spinner.Hide();
            }

            // Git dropdown
            m_GitDropdown.SetEnabled(!isBusy);
            if (isLoading)
            {
                m_GitDropdown.choices = new List<string> { k_LoadingText };
                m_GitDropdown.SetValueWithoutNotify(k_LoadingText);
            }
            else
            {
                BuildDropdownChoices();
                var idx = m_DropdownTypes.IndexOf(gitType);
                if (idx >= 0)
                {
                    m_GitDropdown.SetValueWithoutNotify(m_GitDropdown.choices[idx]);
                }
            }

            // Custom path
            m_CustomPathContainer.SetDisplay(!isLoading && gitType == GitInstanceType.Custom);
            m_CustomPathField.SetValueWithoutNotify(AssistantProjectPreferences.CustomGitPath);

            // Validation display
            var hasError = !string.IsNullOrEmpty(m_ErrorMessage);
            var showValidation = !isBusy && !hasError && validation.IsValid;
            var showError = !isBusy && (hasError || !validation.IsValid);

            m_ValidationContainer.SetDisplay(showValidation);
            m_ErrorContainer.SetDisplay(showError);

            if (showValidation)
            {
                m_ValidationLabel.text = $"Git {validation.GitVersion}, LFS {validation.LfsVersion}";
                m_ValidationLabel.AddToClassList(k_ValidationSuccessClass);
            }
            else
            {
                m_ValidationLabel.RemoveFromClassList(k_ValidationSuccessClass);
            }

            if (showError)
            {
                m_ErrorText.text = hasError ? m_ErrorMessage : validation.ErrorMessage;
            }

            m_EnableToggle.SetValueWithoutNotify(isInitializing || checkpointEnabled);
            m_EnableToggle.SetEnabled(!isBusy && (validation.IsValid || checkpointEnabled));
            m_DisclaimerContainer.SetDisplay(checkpointEnabled);
            m_RepoLocationField.SetValueWithoutNotify(
                AssistantCheckpoints.IsInitialized ? AssistantCheckpoints.RepositoryPath : "Not initialized");
        }

        void BuildDropdownChoices()
        {
            var choices = new List<string>();
            m_DropdownTypes.Clear();

            if (EmbeddedAvailable)
            {
                choices.Add($"Embedded (git {s_EmbeddedVersion})");
                m_DropdownTypes.Add(GitInstanceType.Embedded);
            }

            if (s_SystemFound)
            {
                choices.Add(string.IsNullOrEmpty(s_SystemVersion) ? "System" : $"System (git {s_SystemVersion})");
                m_DropdownTypes.Add(GitInstanceType.System);
            }

            choices.Add(k_CustomText);
            m_DropdownTypes.Add(GitInstanceType.Custom);

            m_GitDropdown.choices = choices;
        }

        async void DetectGitAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    s_EmbeddedVersion = GitInstanceResolver.GetEmbeddedGitVersion();
                    var sys = GitInstanceResolver.DetectSystemGit();
                    s_SystemFound = sys.found;
                    s_SystemVersion = sys.version;
                    s_DetectionComplete = true;
                });

                MainThread.DispatchAndForget(() =>
                {
                    if (panel == null) return;

                    m_IsLoading = false;
                    m_ErrorMessage = null;
                    EnsureValidSelection();
                    RefreshUI();
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Checkpoint] Failed to detect git: {ex.Message}");
                MainThread.DispatchAndForget(() =>
                {
                    if (panel == null) return;

                    m_IsLoading = false;
                    m_ErrorMessage = "Failed to detect git. Please specify a custom path.";
                    m_DropdownTypes = new List<GitInstanceType> { GitInstanceType.Custom };
                    AssistantProjectPreferences.GitInstanceType = GitInstanceType.Custom;
                    RefreshUI();
                });
            }
        }

        static void EnsureValidSelection()
        {
            var current = AssistantProjectPreferences.GitInstanceType;
            if (IsAvailable(current))
            {
                return;
            }

            AssistantProjectPreferences.GitInstanceType =
                s_SystemFound ? GitInstanceType.System :
                EmbeddedAvailable ? GitInstanceType.Embedded :
                GitInstanceType.Custom;
        }

        static bool IsAvailable(GitInstanceType type) => type switch
        {
            GitInstanceType.Embedded => EmbeddedAvailable,
            GitInstanceType.System => s_SystemFound,
            _ => true
        };

        static GitValidationResult GetValidation(bool useCache)
        {
            var type = AssistantProjectPreferences.GitInstanceType;
            var customPath = AssistantProjectPreferences.CustomGitPath;

            if (type == GitInstanceType.Custom && s_LastCustomPath != customPath)
            {
                k_ValidationCache.Remove(GitInstanceType.Custom);
                s_LastCustomPath = customPath;
            }

            if (useCache && k_ValidationCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var result = AssistantCheckpoints.ValidateGitInstance();
            k_ValidationCache[type] = result;
            s_LastCustomPath = customPath;
            return result;
        }

        async void OnEnableToggled(ChangeEvent<bool> evt)
        {
            if (m_IsInitializing)
            {
                return;
            }

            if (evt.newValue)
            {
                var validation = GetValidation(useCache: false);
                if (!validation.IsValid)
                {
                    RefreshUI();
                    return;
                }

                m_IsInitializing = true;
                RefreshUI();

                try
                {
                    var result = await AssistantCheckpoints.InitializeAsync();
                    if (result.Success)
                    {
                        AssistantProjectPreferences.CheckpointEnabled = true;
                        AssistantEvents.Send(new EventCheckpointEnableStateChanged(true));
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Checkpoint] Failed: {ex.Message}");
                }
                finally
                {
                    m_IsInitializing = false;
                    RefreshUI();
                }
            }
            else
            {
                AssistantProjectPreferences.CheckpointEnabled = false;
                RefreshUI();
                AssistantEvents.Send(new EventCheckpointEnableStateChanged(false));
            }
        }

        void OnGitTypeChanged()
        {
            k_ValidationCache.Remove(AssistantProjectPreferences.GitInstanceType);
            m_ErrorMessage = null;
            RefreshUI();
        }

        void OnGitDropdownChanged(ChangeEvent<string> evt)
        {
            var idx = m_GitDropdown.choices.IndexOf(evt.newValue);
            if (idx < 0 || idx >= m_DropdownTypes.Count)
            {
                return;
            }

            var type = m_DropdownTypes[idx];
            k_ValidationCache.Remove(type);
            m_ErrorMessage = null;
            AssistantProjectPreferences.GitInstanceType = type;
            RefreshUI();
        }

        void OnCustomPathFocusOut(FocusOutEvent evt)
        {
            ApplyCustomPath();
        }

        void OnCustomPathKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                ApplyCustomPath();
                evt.StopPropagation();
            }
        }

        void ApplyCustomPath()
        {
            var newPath = m_CustomPathField.value;
            if (AssistantProjectPreferences.CustomGitPath == newPath)
            {
                return;
            }

            AssistantProjectPreferences.CustomGitPath = newPath;
            k_ValidationCache.Remove(GitInstanceType.Custom);
            m_ErrorMessage = null;
            RefreshUI();
        }

        void OnBrowseGitPath(PointerUpEvent evt)
        {
            var currentPath = AssistantProjectPreferences.CustomGitPath;
            var directory = string.IsNullOrEmpty(currentPath) ? "" : Path.GetDirectoryName(currentPath);

#if UNITY_EDITOR_WIN
            var extension = "exe";
#else
            var extension = "";
#endif

            var selectedPath = EditorUtility.OpenFilePanel("Select Git Executable", directory, extension);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                m_CustomPathField.value = selectedPath;
            }
        }

        static void OnOpenRepo(PointerUpEvent evt)
        {
            if (AssistantCheckpoints.IsInitialized)
            {
                EditorUtility.RevealInFinder(AssistantCheckpoints.RepositoryPath);
            }
        }
    }
}
