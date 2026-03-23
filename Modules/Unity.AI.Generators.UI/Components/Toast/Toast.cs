using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Toolkit.Accounts.Components;
using Unity.AI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    static class Toast
    {
        class ToastState
        {
            public bool IsToastShowing;
            public string LastMessage = string.Empty;
            public VisualElement CurrentToast;
            public BasicBannerContent CurrentBanner;
        }

        static readonly Dictionary<VisualElement, ToastState> k_ToastStates = new();

        // Colors
        static readonly Color k_NormalColor = new(0.35f, 0.35f, 0.35f, 0.9f);
        static readonly Color k_HoverColor = new(0.45f, 0.45f, 0.45f, 0.95f);
        static readonly Color k_HighlightColor = new(0.55f, 0.55f, 0.35f, 0.95f); // Yellowish highlight

        /// <summary>
        /// Shows a toast on the given parent VisualElement asynchronously.
        /// If a toast is already active on the same parent, this call awaits until it is done.
        /// If the same message is already showing, it briefly highlights the current toast.
        /// </summary>
        public static async void ShowToast(this VisualElement parent, string message)
        {
            if (!k_ToastStates.TryGetValue(parent, out var state))
            {
                state = new ToastState();
                k_ToastStates[parent] = state;
            }

            if (message == state.LastMessage && state.IsToastShowing)
            {
                // Highlight the existing toast temporarily
                await HighlightCurrentToast(state);
                return;
            }

            while (state.IsToastShowing)
                await EditorTask.Yield();

            state.IsToastShowing = true;
            state.LastMessage = message;

            var tcs = new TaskCompletionSource<bool>();

            try
            {
                var toast = new VisualElement { style = {
                    position = Position.Absolute,
                    flexDirection = FlexDirection.Row,
                    left = 0, right = 0,
                    bottom = 2,
                    marginLeft = 2,
                    marginRight = 2,
                    height = 40,
                    opacity = 1f,
                    transitionDuration = new List<TimeValue> { new(500, TimeUnit.Millisecond) },
                    transitionProperty = new List<StylePropertyName> { "opacity" }
                }};

                var bannerContent = new BasicBannerContent(message) { style = {
                    flexGrow = 1,
                    backgroundColor = k_NormalColor
                }};

                toast.Add(bannerContent);

                // Store references for potential highlighting later
                state.CurrentToast = toast;
                state.CurrentBanner = bannerContent;

                // Add hover effect
                toast.RegisterCallback<MouseEnterEvent>(_ => {
                    bannerContent.style.backgroundColor = k_HoverColor;
                });

                toast.RegisterCallback<MouseLeaveEvent>(_ => {
                    bannerContent.style.backgroundColor = k_NormalColor;
                });

                // Add click handler
                toast.RegisterCallback<ClickEvent>(e => {
                    toast.style.opacity = 0f;
                    _ = EditorTask.Delay(500).ContinueWith(_ => tcs.SetResult(true));
                });

                parent.Add(toast);

                // Wait for click to complete
                await tcs.Task;
            }
            finally
            {
                state.CurrentToast?.RemoveFromHierarchy();
                state.CurrentToast = null;
                state.CurrentBanner = null;
                state.LastMessage = string.Empty;
                state.IsToastShowing = false;
            }
        }

        static async Task HighlightCurrentToast(ToastState state)
        {
            if (state.CurrentBanner == null)
                return;

            // Save the original color
            var originalColor = state.CurrentBanner.style.backgroundColor.value;

            // Pulse effect: change to highlight color, wait, then back to original
            state.CurrentBanner.style.backgroundColor = k_HighlightColor;
            await EditorTask.Delay(500);
            state.CurrentBanner.style.backgroundColor = originalColor;
        }
    }
}
