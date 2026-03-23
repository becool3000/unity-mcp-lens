using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Editor.SessionBanner;
using Unity.AI.Assistant.Editor.Settings;
using Unity.Relay.Editor;
using Unity.Relay.Editor.Acp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;
using Debug = UnityEngine.Debug;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    [InitializeOnLoad]
    class AcpInstallDialogWindow : EditorWindow
    {
        const float k_WindowWidth = 520f;
        const float k_WindowHeight = 360f;

        const string k_ViewPath = AssistantUIConstants.UIModulePath + AssistantUIConstants.ViewFolder + "AcpInstallDialogView.uxml";

        AcpProviderDescriptor m_Provider;
        string m_ProviderId;
        string m_ProviderName;
        string m_Platform;
        AcpInstallStep m_Step;

        Label m_TitleLabel;
        Label m_DescriptionLabel;
        VisualElement m_CommandsContainer;
        VisualElement m_ApiKeyContainer;
        TextField m_ApiKeyField;
        Label m_StatusLabel;
        Button m_InstallButton;
        Button m_CancelButton;

        // Prerequisites UI elements
        VisualElement m_PrerequisitesContainer;
        Label m_PrerequisitesMessageLabel;
        Label m_PrerequisitesErrorLabel;
        AcpPrerequisiteCheck[] m_PrerequisiteChecks;

        Process m_InstallProcess;
        SynchronizationContext m_MainThreadContext;

        static AcpInstallDialogWindow()
        {
            AcpSessionStatusBannerProvider.OnInstallDialogRequested += Show;
        }

        public static void Show(AcpProviderDescriptor provider, string platform, AcpInstallStep step)
        {
            if (provider == null || step == null)
                return;

            var window = CreateInstance<AcpInstallDialogWindow>();
            window.m_Provider = provider;
            window.m_ProviderId = provider.Id;
            window.m_ProviderName = string.IsNullOrEmpty(provider.DisplayName) ? provider.Id : provider.DisplayName;
            window.m_Platform = platform;
            window.m_Step = step;
            window.m_PrerequisiteChecks = provider.GetPrerequisiteChecks(platform);
            window.titleContent = new GUIContent($"Install {window.m_ProviderName}");

            var size = new Vector2(k_WindowWidth, k_WindowHeight);
            window.minSize = size;
            window.maxSize = size;
            window.position = GetCenteredRect(size);
            window.ShowModalUtility();
        }

        void CreateGUI()
        {
            m_MainThreadContext = SynchronizationContext.Current;

            var root = rootVisualElement;
            root.Clear();

            LoadStyle(root, AssistantUIConstants.UIStylePath + AssistantUIConstants.AssistantBaseStyle);
            LoadStyle(root, AssistantUIConstants.UIStylePath + (EditorGUIUtility.isProSkin
                ? AssistantUIConstants.AssistantSharedStyleDark
                : AssistantUIConstants.AssistantSharedStyleLight) + AssistantUIConstants.StyleExtension);

            var view = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_ViewPath);
            if (view == null)
            {
                Debug.LogError($"Missing install dialog view at {k_ViewPath}.");
                return;
            }

            view.CloneTree(root);
            var dialogRoot = root.Q<VisualElement>(className: "acp-install-dialog");
            if (dialogRoot != null)
            {
                dialogRoot.AddToClassList(EditorGUIUtility.isProSkin ? "theme-dark" : "theme-light");
            }

            m_TitleLabel = root.Q<Label>("titleLabel");
            m_DescriptionLabel = root.Q<Label>("descriptionLabel");
            m_CommandsContainer = root.Q<VisualElement>("commandsContainer");
            var commandLabel = root.Q<Label>("commandLabel");
            var copyButton = root.Q<Button>("copyButton");

            m_ApiKeyContainer = root.Q<VisualElement>("apiKeyContainer");
            m_ApiKeyField = root.Q<TextField>("apiKeyField");

            m_StatusLabel = root.Q<Label>("statusLabel");
            m_InstallButton = root.Q<Button>("installButton");
            m_CancelButton = root.Q<Button>("cancelButton");

            // Query prerequisites elements
            m_PrerequisitesContainer = root.Q<VisualElement>("prerequisitesContainer");
            m_PrerequisitesMessageLabel = root.Q<Label>("prerequisitesMessageLabel");
            m_PrerequisitesErrorLabel = root.Q<Label>("prerequisitesErrorLabel");

            if (m_TitleLabel != null)
                m_TitleLabel.text = $"Install {m_ProviderName}";
            if (m_StatusLabel != null)
                m_StatusLabel.style.display = DisplayStyle.None;

            if (commandLabel != null)
                commandLabel.text = m_Step?.Display ?? string.Empty;
            if (copyButton != null)
                copyButton.clicked += () => GUIUtility.systemCopyBuffer = m_Step?.Display ?? string.Empty;

            if (m_CancelButton != null)
                m_CancelButton.clicked += Close;

            // Determine initial state based on prerequisites
            var unmet = PrerequisiteChecker.GetUnmetPrerequisites(m_PrerequisiteChecks);
            if (unmet.Count > 0)
            {
                ShowPrerequisitesState(unmet);
            }
            else
            {
                ShowInstallState();
            }
        }

        void ShowPrerequisitesState(List<AcpPrerequisiteCheck> unmetPrereqs)
        {
            // Hide install elements
            if (m_CommandsContainer != null)
                m_CommandsContainer.style.display = DisplayStyle.None;

            // Show prerequisites
            if (m_PrerequisitesContainer != null)
                m_PrerequisitesContainer.style.display = DisplayStyle.Flex;

            if (m_PrerequisitesMessageLabel != null)
            {
                // Build combined message for all unmet prerequisites
                var sb = new StringBuilder();
                foreach (var prereq in unmetPrereqs)
                {
                    if (sb.Length > 0)
                        sb.Append("\n\n");
                    sb.Append(prereq.Message);
                    if (!string.IsNullOrEmpty(prereq.HelpUrl))
                    {
                        sb.Append($"\n<a href=\"{prereq.HelpUrl}\"><color=#7BAEFA>Download and install</color></a>");
                    }
                }
                m_PrerequisitesMessageLabel.text = sb.ToString();
                m_PrerequisitesMessageLabel.RegisterCallback<PointerDownLinkTagEvent>(OnLinkClicked);
            }

            if (m_PrerequisitesErrorLabel != null)
                m_PrerequisitesErrorLabel.style.display = DisplayStyle.None;

            // Transform Install button to Verify
            if (m_InstallButton != null)
            {
                m_InstallButton.text = "Verify";
                m_InstallButton.clicked -= OnInstallClicked;
                m_InstallButton.clicked -= OnVerifyClicked;
                m_InstallButton.clicked += OnVerifyClicked;
                m_InstallButton.SetEnabled(true);
            }

            if (m_DescriptionLabel != null)
                m_DescriptionLabel.text = "Prerequisites must be installed before continuing.";
        }

        void ShowInstallState()
        {
            // Hide prerequisites
            if (m_PrerequisitesContainer != null)
                m_PrerequisitesContainer.style.display = DisplayStyle.None;

            // Show install elements
            if (m_CommandsContainer != null)
                m_CommandsContainer.style.display = DisplayStyle.Flex;

            // Reset button
            if (m_InstallButton != null)
            {
                m_InstallButton.text = "Install";
                m_InstallButton.clicked -= OnVerifyClicked;
                m_InstallButton.clicked -= OnInstallClicked;
                m_InstallButton.clicked += OnInstallClicked;
                m_InstallButton.SetEnabled(true);
            }

            if (m_DescriptionLabel != null)
                m_DescriptionLabel.text = "Review the command below. The Gateway will run it locally and stop on failure. You can copy and run it yourself instead.";
        }

        void OnVerifyClicked()
        {
            var unmet = PrerequisiteChecker.GetUnmetPrerequisites(m_PrerequisiteChecks);
            if (unmet.Count == 0)
            {
                ShowInstallState();
            }
            else
            {
                // Update message to show remaining unmet prerequisites
                ShowPrerequisitesState(unmet);
                if (m_PrerequisitesErrorLabel != null)
                {
                    m_PrerequisitesErrorLabel.text = "Some prerequisites are still missing. Please install and try again.";
                    m_PrerequisitesErrorLabel.style.display = DisplayStyle.Flex;
                }
            }
        }

        void OnInstallClicked()
        {
            if (string.IsNullOrEmpty(m_ProviderId) || string.IsNullOrEmpty(m_Platform))
            {
                ShowInstallResult(new AcpInstallResult
                {
                    Ok = false,
                    Error = "Missing provider or platform."
                });
                return;
            }

            if (m_Step?.Exec?.Command == null)
            {
                ShowInstallResult(new AcpInstallResult
                {
                    Ok = false,
                    Error = "No install command available."
                });
                return;
            }

            var args = JoinProcessArguments(m_Step.Exec.Args);
            var startInfo = new ProcessStartInfo
            {
                FileName = m_Step.Exec.Command,
                Arguments = args,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            m_InstallProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            m_InstallProcess.Exited += OnProcessExited;

            SetRunningState(true);

            try
            {
                m_InstallProcess.Start();
            }
            catch (System.Exception ex)
            {
                ShowInstallResult(new AcpInstallResult
                {
                    Ok = false,
                    Error = $"Failed to start process: {ex.Message}"
                });
                CleanupProcess();
            }
        }

        void SetRunningState(bool isRunning)
        {
            m_InstallButton?.SetEnabled(!isRunning);

            if (isRunning && m_CancelButton != null)
                m_CancelButton.style.display = DisplayStyle.None;

            if (m_StatusLabel != null)
            {
                m_StatusLabel.text = isRunning ? "Running install in terminal..." : string.Empty;
                m_StatusLabel.style.display = DisplayStyle.Flex;
            }
        }

        void ShowInstallResult(AcpInstallResult result)
        {
            if (m_StatusLabel == null)
                return;

            m_StatusLabel.style.display = DisplayStyle.Flex;
            if (result.Ok)
            {
                // Ensures preferences are up-to-date. Actually not the correct way to do this.
                // but there is currently not awaitable "refresh". It should instead be done prior to saving them.
                GatewayPreferenceService.Instance.Preferences.Refresh();

                m_StatusLabel.text = string.Empty;
                m_StatusLabel.style.display = DisplayStyle.None;

                // Update window and title
                titleContent = new GUIContent($"{m_ProviderName} Installed");
                if (m_TitleLabel != null)
                    m_TitleLabel.text = $"{m_ProviderName} has been installed.";

                // Hide command section
                if (m_CommandsContainer != null)
                    m_CommandsContainer.style.display = DisplayStyle.None;

                // Check for post-install info and determine mode
                var postInstall = m_Provider?.PostInstall;
                if (postInstall != null && postInstall.IsLoginMode)
                {
                    // Login mode (Cursor): Show Proceed button that runs login command
                    ShowLoginMode(postInstall);
                }
                else if (postInstall != null && postInstall.IsApiKeyMode)
                {
                    // API Key mode (Claude/Codex): Show input field + Save button
                    ShowApiKeyMode(postInstall);
                }
                else
                {
                    // No post-install info, just show done
                    ShowDoneMode();
                }
            }
            else
            {
                var errorMessage = string.IsNullOrEmpty(result.Error) ? "Unknown error." : result.Error;
                var failedStep = string.IsNullOrEmpty(result.FailedStep) ? "" : $" Failed at: {result.FailedStep}";
                m_StatusLabel.text = $"Install failed. {errorMessage}{failedStep}";

                // Transform install button to Back button
                if (m_InstallButton != null)
                {
                    m_InstallButton.text = "Back";
                    m_InstallButton.clicked -= OnInstallClicked;
                    m_InstallButton.clicked += Close;
                    m_InstallButton.SetEnabled(true);
                }
            }
        }

        void ShowLoginMode(AcpPostInstallInfo postInstall)
        {
            // Show post-install message with clickable links
            if (m_DescriptionLabel != null)
            {
                m_DescriptionLabel.text = postInstall.Message;
                m_DescriptionLabel.RegisterCallback<PointerDownLinkTagEvent>(OnLinkClicked);
            }

            // Hide API key input (not needed for login mode)
            if (m_ApiKeyContainer != null)
                m_ApiKeyContainer.style.display = DisplayStyle.None;

            // Transform install button to Proceed button
            if (m_InstallButton != null)
            {
                m_InstallButton.text = "Proceed";
                m_InstallButton.clicked -= OnInstallClicked;
                m_InstallButton.clicked += () => ExecuteLoginAndClose(postInstall.LoginExec);
                m_InstallButton.SetEnabled(true);
            }
        }

        void ShowApiKeyMode(AcpPostInstallInfo postInstall)
        {
            // Show post-install message with clickable links
            if (m_DescriptionLabel != null)
            {
                m_DescriptionLabel.text = postInstall.Message;
                m_DescriptionLabel.RegisterCallback<PointerDownLinkTagEvent>(OnLinkClicked);
            }

            // Show API key input
            if (m_ApiKeyContainer != null)
                m_ApiKeyContainer.style.display = DisplayStyle.Flex;

            // Setup API key field submission
            if (m_ApiKeyField != null)
            {
                m_ApiKeyField.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    {
                        SaveApiKeyAndClose(postInstall.EnvVarName, m_ApiKeyField.value);
                    }
                });
                m_ApiKeyField.Focus();
            }

            // Transform install button to Save button
            if (m_InstallButton != null)
            {
                m_InstallButton.text = "Save";
                m_InstallButton.clicked -= OnInstallClicked;
                m_InstallButton.clicked += () => SaveApiKeyAndClose(postInstall.EnvVarName, m_ApiKeyField?.value);
                m_InstallButton.SetEnabled(true);
            }
        }

        void ShowDoneMode()
        {
            if (m_DescriptionLabel != null)
                m_DescriptionLabel.text = $"{m_ProviderName} is ready to use.";

            // Transform install button to Done button
            if (m_InstallButton != null)
            {
                m_InstallButton.text = "Done";
                m_InstallButton.clicked -= OnInstallClicked;
                m_InstallButton.clicked += OnDoneClicked;
                m_InstallButton.SetEnabled(true);
            }

            // Trigger banner refresh
            RefreshBanner();
        }

        void OnDoneClicked()
        {
            var providerId = m_ProviderId; // Capture for delayed callback
            Close();

            // Restart session after dialog closes
            EditorApplication.delayCall += () =>
            {
                var assistantWindow = AssistantWindow.ShowWindow();
                if (assistantWindow?.m_Context != null)
                {
                    _ = assistantWindow.m_Context.SwitchProviderAsync(providerId);
                }
            };
        }

        void ExecuteLoginAndClose(AcpPostInstallLoginExec loginExec)
        {
            if (loginExec == null)
            {
                var providerId = m_ProviderId; // Capture for delayed callback
                RefreshBanner();
                Close();

                // Restart session even if no login command is configured
                EditorApplication.delayCall += () =>
                {
                    var assistantWindow = AssistantWindow.ShowWindow();
                    if (assistantWindow?.m_Context != null)
                    {
                        _ = assistantWindow.m_Context.SwitchProviderAsync(providerId);
                    }
                };
                return;
            }

            var args = JoinProcessArguments(loginExec.Args);
            var startInfo = new ProcessStartInfo
            {
                FileName = loginExec.Command,
                Arguments = args,
                UseShellExecute = true, // Required for interactive login (may open browser)
                CreateNoWindow = false
            };

            // Reuse the existing install process tracking
            m_InstallProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            m_InstallProcess.Exited += OnLoginProcessExited;

            SetLoginRunningState(true);

            try
            {
                m_InstallProcess.Start();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to start login process: {ex.Message}\nCommand: '{loginExec.Command}'\nArguments: '{args}'\nExists: {System.IO.File.Exists(loginExec.Command)}");
                SetLoginRunningState(false);
                RefreshBanner();
                Close();
            }
        }

        void SetLoginRunningState(bool isRunning)
        {
            m_InstallButton?.SetEnabled(!isRunning);

            if (m_StatusLabel != null)
            {
                m_StatusLabel.text = isRunning ? "Waiting for login to complete..." : string.Empty;
                m_StatusLabel.style.display = isRunning ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        void OnLoginProcessExited(object sender, System.EventArgs e)
        {
            var exitCode = m_InstallProcess?.ExitCode ?? -1;
            var providerId = m_ProviderId; // Capture for delayed callback
            m_MainThreadContext?.Post(_ =>
            {
                // Unregister this handler before cleanup
                if (m_InstallProcess != null)
                    m_InstallProcess.Exited -= OnLoginProcessExited;

                CleanupProcess();

                if (exitCode != 0)
                    Debug.LogWarning($"Login process exited with code {exitCode}");

                // Refresh banner regardless of exit code to re-check status
                RefreshBanner();
                Close();

                // Restart session after login completes
                EditorApplication.delayCall += () =>
                {
                    var assistantWindow = AssistantWindow.ShowWindow();
                    if (assistantWindow?.m_Context != null)
                    {
                        _ = assistantWindow.m_Context.SwitchProviderAsync(providerId);
                    }
                };
            }, null);
        }

        void OnLinkClicked(PointerDownLinkTagEvent evt)
        {
            if (!string.IsNullOrEmpty(evt.linkID))
                Application.OpenURL(evt.linkID);
        }

        void SaveApiKeyAndClose(string envVarName, string apiKey)
        {
            if (string.IsNullOrEmpty(envVarName) || string.IsNullOrEmpty(apiKey))
            {
                Close();
                return;
            }

            var prefs = GatewayPreferenceService.Instance.Preferences.Value;
            var providerInfo = prefs.ProviderInfoList.FirstOrDefault(provider => provider.ProviderType == m_ProviderId);
            var current = providerInfo.Variables.FirstOrDefault(env => env.Name == envVarName);
            if (current == null)
                providerInfo.Variables.Add(new(envVarName, apiKey, true) {IsUpdated = true});
            else
            {
                current.Value = apiKey;
                current.IsUpdated = true;
            }

            GatewayPreferenceService.Instance.Preferences.Value = GatewayPreferenceService.Instance.Preferences.Value with { };

            // Trigger banner refresh
            RefreshBanner();

            // Close this dialog first, then switch to the provider and start a session
            Close();

            // Use delayCall to ensure the dialog is fully closed before switching providers
            EditorApplication.delayCall += () =>
            {
                var assistantWindow = AssistantWindow.ShowWindow();
                if (assistantWindow?.m_Context != null)
                {
                    _ = assistantWindow.m_Context.SwitchProviderAsync(m_ProviderId);
                }
            };
        }

        void RefreshBanner()
        {
            ExecutableAvailabilityState.ClearCache(m_ProviderId);
            ExecutableAvailabilityState.RequestValidation(m_ProviderId);
        }

        static void LoadStyle(VisualElement root, string path)
        {
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (style != null)
            {
                root.styleSheets.Add(style);
                return;
            }

            var themeStyle = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
            if (themeStyle != null)
            {
                root.styleSheets.Add(themeStyle);
            }
        }

        void OnProcessExited(object sender, System.EventArgs e)
        {
            var exitCode = m_InstallProcess?.ExitCode ?? -1;
            m_MainThreadContext?.Post(_ =>
            {
                ShowInstallResult(new AcpInstallResult
                {
                    Ok = exitCode == 0,
                    Error = exitCode != 0 ? $"Process exited with code {exitCode}" : null
                });
                CleanupProcess();
            }, null);
        }

        void CleanupProcess()
        {
            if (m_InstallProcess == null)
                return;

            m_InstallProcess.Exited -= OnProcessExited;
            m_InstallProcess.Dispose();
            m_InstallProcess = null;
        }

        void OnDestroy()
        {
            if (m_InstallProcess != null && !m_InstallProcess.HasExited)
            {
                m_InstallProcess.Kill();
            }
            CleanupProcess();
        }

        static readonly char[] k_ShellSpecialChars = { ' ', '\t', '"', '|', '&', ';', '\'', '$', '`', '(', ')', '<', '>' };

        /// <summary>
        /// Joins an args array into a properly quoted arguments string.
        /// Args containing shell-special characters are double-quoted so that
        /// the process argument parser preserves them as single arguments
        /// (critical for bash -c to receive the full command string).
        /// </summary>
        static string JoinProcessArguments(string[] args)
        {
            if (args == null || args.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var arg in args)
            {
                if (sb.Length > 0) sb.Append(' ');

                if (arg.IndexOfAny(k_ShellSpecialChars) < 0)
                {
                    sb.Append(arg);
                    continue;
                }

                sb.Append('"');
                foreach (char c in arg)
                {
                    if (c is '"' or '\\')
                        sb.Append('\\');
                    sb.Append(c);
                }
                sb.Append('"');
            }
            return sb.ToString();
        }

        static Rect GetCenteredRect(Vector2 size)
        {
            var editorMainWindowRect = EditorGUIUtility.GetMainWindowPosition();
            return new Rect(
                editorMainWindowRect.x + (editorMainWindowRect.width - size.x) * 0.5f,
                editorMainWindowRect.y + (editorMainWindowRect.height - size.y) * 0.5f,
                size.x,
                size.y
            );
        }
    }
}
