using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI.Utilities
{
    static class DialogUtilities
    {
        /// <summary>
        /// Shows a non-blocking dialog about interrupted downloads asynchronously.
        /// </summary>
        /// <param name="interruptedDownloads">The collection of interrupted downloads</param>
        /// <param name="onOptionSelected"></param>
        /// <returns>A task that completes when the user makes a choice, with a tuple containing whether there are
        /// interrupted downloads and which option was selected</returns>
        public static async Task<bool> ShowResumeDownloadPopup(IEnumerable<IInterruptedDownloadBase> interruptedDownloads, Action<int> onOptionSelected)
        {
            var hasInterruptedDownloads = false;

            // Track already processed unique task IDs to avoid duplicate checks
            var processedUniqueIds = new HashSet<string>();

            foreach (var data in interruptedDownloads)
            {
                // Skip if we've already processed this unique task ID
                if (!string.IsNullOrEmpty(data.uniqueTaskId) && !processedUniqueIds.Add(data.uniqueTaskId))
                    continue;

                // Check using the progress task ID from the interface
                if (!Progress.Exists(data.progressTaskId))
                {
                    hasInterruptedDownloads = true;
                    break;
                }

                var progressStatus = Progress.GetStatus(data.progressTaskId);
                if (progressStatus is Progress.Status.Paused or Progress.Status.Failed or Progress.Status.Canceled)
                {
                    hasInterruptedDownloads = true;
                    break;
                }
            }

            if (hasInterruptedDownloads && interruptedDownloads.Any())
            {
                var option = await ResumeDownloadDialogWindow.ShowAsync(interruptedDownloads.Count());
                onOptionSelected?.Invoke(option);
                return true;
            }

            onOptionSelected?.Invoke(0);
            return false;
        }

        /// <summary>
        /// Asynchronously prompts the user about replacing the given asset with a non-blocking dialog.
        /// </summary>
        /// <param name="asset">Asset reference for the asset to replace.</param>
        /// <param name="rememberedChoice">If true, user has already allowed replacement.</param>
        /// <param name="rememberChoice"></param>
        /// <param name="otherPath">Optional path to the "new" generated output. May be null.</param>
        /// <returns>A task that completes when the user makes a choice, with a bool indicating if replacement is allowed</returns>
        public static async Task<bool> ConfirmReplaceAsset(
            AssetReference asset,
            bool rememberedChoice,
            Action<bool> rememberChoice,
            string otherPath = null)
        {
            // If user previously selected "Always" for this asset, no need to show dialog
            if (rememberedChoice)
            {
                rememberChoice?.Invoke(true);
                return true;
            }

            var thisPath = asset.GetPath();
            var thisFileName = Path.GetFileNameWithoutExtension(thisPath);
            var thisExtension = Path.GetExtension(thisPath) ?? string.Empty;
            var otherExtension = !string.IsNullOrEmpty(otherPath) ? Path.GetExtension(otherPath) : string.Empty;
            var mismatchedTypes = !string.Equals(thisExtension, otherExtension, StringComparison.InvariantCultureIgnoreCase);

            // Build the dialog message
            var dialogTitle = $"Replace {thisFileName}?";
            var dialogMessage = $"Replace asset '{thisFileName}' with its newly generated version?\n\nTo preserve your current asset and still use the selected generation press 'no' and duplicate the asset in the project window or right-click the generation and promote it to a new asset.";
            if (!string.IsNullOrEmpty(otherPath) && mismatchedTypes)
                dialogMessage += $"\n\nA conversion will occur because the file types differ: '{otherExtension}' → '{thisExtension}'.";

            var option = await ReplaceAssetDialogWindow.ShowAsync(dialogTitle, dialogMessage, null);

            switch (option)
            {
                case 0: // "Yes" selected
                    rememberChoice?.Invoke(false);
                    return true;
                case 1: // "No" selected
                    rememberChoice?.Invoke(false);
                    return false;
                case 2: // "Always" selected
                    rememberChoice?.Invoke(true);
                    return true;
                default:
                    rememberChoice?.Invoke(false);
                    return false;
            }
        }

        /// <summary>
        /// Base class for asynchronous dialog windows
        /// </summary>
        abstract class AsyncDialogWindow : EditorWindow
        {
            TaskCompletionSource<int> m_TaskCompletionSource;
            string m_Message;
            string[] m_ButtonLabels;
            int m_DefaultOptionIndex;

            /// <summary>
            /// Shows a dialog window asynchronously
            /// </summary>
            /// <param name="title">The window title</param>
            /// <param name="message">The message to display</param>
            /// <param name="buttonLabels">Labels for the buttons</param>
            /// <param name="defaultButtonIndex">The button index to use when window is closed without selection</param>
            /// <returns>Task that resolves to the selected button index</returns>
            public static Task<int> ShowAsync<T>(string title, string message, string[] buttonLabels, int defaultButtonIndex = 0) where T : AsyncDialogWindow
            {
                var countLines = message.Count(c => c == '\n');

                var window = EditorWindowExtensions.CreateWindow<T>(null, title, false);
                window.m_Message = message;
                window.m_ButtonLabels = buttonLabels;
                window.m_DefaultOptionIndex = defaultButtonIndex;

                // Set window size based on content
                float minWidth = 400;
                float minHeight = 120 + countLines * 24;
                var buttonCount = buttonLabels.Length;

                // Add width for buttons (approximation)
                minWidth = Mathf.Max(minWidth, buttonCount * 120);

                window.minSize = new Vector2(minWidth, minHeight);
                window.maxSize = new Vector2(minWidth, minHeight);

                window.Init();
                window.ShowAuxWindow();

                var tcs = new TaskCompletionSource<int>();
                window.m_TaskCompletionSource = tcs;
                return tcs.Task;
            }

            void Init()
            {
                rootVisualElement.Clear();

                var root = rootVisualElement;
                root.style.paddingBottom = 16;
                root.style.paddingTop = 16;
                root.style.paddingLeft = 16;
                root.style.paddingRight = 16;

                // Message
                var message = new Label(m_Message)
                {
                    style =
                    {
                        whiteSpace = WhiteSpace.Normal,
                        marginBottom = 24
                    }
                };
                root.Add(message);

                // Buttons container
                var buttonsContainer = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        justifyContent = Justify.FlexEnd,
                        flexShrink = 0
                    }
                };
                root.Add(buttonsContainer);

                // Add buttons
                for (var i = 0; i < m_ButtonLabels.Length; i++)
                {
                    var buttonIndex = i; // Capture for lambda
                    var button = new Button(() => {
                        m_TaskCompletionSource?.TrySetResult(buttonIndex);
                        Close();
                    }) { text = m_ButtonLabels[i], style = { flexShrink = 0 }};

                    // Add margin to all but the last button
                    if (i < m_ButtonLabels.Length - 1)
                        button.style.marginRight = 8;

                    buttonsContainer.Add(button);
                }
            }

            protected void OnDestroy()
            {
                // If window is closed without clicking a button
                m_TaskCompletionSource?.TrySetResult(m_DefaultOptionIndex);
            }
        }

        /// <summary>
        /// Dialog window for resuming downloads
        /// </summary>
        class ResumeDownloadDialogWindow : AsyncDialogWindow
        {
            public static Task<int> ShowAsync(int downloadCount)
            {
                var message = $"Found {downloadCount} interrupted download(s).\nDo you want to resume them, delete them, or skip?";
                var buttons = new[] { "Resume", "Delete", "Skip" };
                return AsyncDialogWindow.ShowAsync<ResumeDownloadDialogWindow>("Interrupted Downloads", message, buttons, 0);
            }
        }

        /// <summary>
        /// Dialog window for asset replacement confirmation
        /// </summary>
        class ReplaceAssetDialogWindow : AsyncDialogWindow
        {
            public static Task<int> ShowAsync(string title, string message, string[] buttons = null)
            {
                buttons ??= new[] { "Yes", "No", "Always for this asset" };
                return AsyncDialogWindow.ShowAsync<ReplaceAssetDialogWindow>(title, message, buttons, 1);
            }
        }
    }
}
