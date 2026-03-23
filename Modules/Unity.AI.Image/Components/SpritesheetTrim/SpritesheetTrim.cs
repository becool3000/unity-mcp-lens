using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class SpritesheetTrim : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/SpritesheetTrim/SpritesheetTrim.uxml";
        const int k_DebounceResumePlaybackMs = 500;

        // MultiSlider control indices for clarity
        const int k_CurrentTimeIndex = 0;
        const int k_StartTimeIndex = 1;
        const int k_EndTimeIndex = 2;

        TextureResult m_OriginalClip;
        float m_StartTimeNormalized;
        float m_EndTimeNormalized;
        double m_OriginalClipLength = 1.0;
        bool m_IsUpdatingSlider; // Prevents recursive updates
        bool m_IsDraggingSlider; // Prevents playback resume during drag
        CancellationTokenSource m_ResumePlaybackCts;
        CancellationTokenSource m_UpdateDurationCts;

        readonly VideoPreview m_Preview;
        readonly MultiSlider m_TimeSlider;
        readonly Button m_ApplyButton;

        public SpritesheetTrim()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            SetEnabled(false);

            // Bind to Redux state
            this.Use(state => state.SelectTrimStartTime(this), UpdateStartTime);
            this.Use(state => state.SelectTrimEndTime(this), UpdateEndTime);

            // Query UI elements
            m_Preview = this.Q<VideoPreview>();
            m_TimeSlider = this.Q<MultiSlider>("time-slider");
            m_ApplyButton = this.Q<Button>("Apply");

            // Register callbacks
            m_ApplyButton.clicked += () => _ = OnApplyButtonClick();
            m_TimeSlider.RegisterValueChangedCallback(OnTimeSliderValueChanged);
            m_TimeSlider.controlDragStarted += OnControlDragStarted;
            m_TimeSlider.controlDragEnded += OnControlDragEnded;
            
            // Listen for time updates from VideoPreview to update the slider thumb
            m_Preview.CurrentTimeChanged += OnPreviewTimeUpdated;
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            // Configure slider: Current time is visual-only, start/end are draggable
            m_TimeSlider.controlCount = 3;
            m_TimeSlider.draggableControls = new[] { false, true, true };

            this.Use(state => state.SelectSelectedGeneration(this), SetSelectedGeneration);
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            m_ResumePlaybackCts?.Cancel();
            m_ResumePlaybackCts?.Dispose();
            m_ResumePlaybackCts = null;

            m_UpdateDurationCts?.Cancel();
            m_UpdateDurationCts?.Dispose();
            m_UpdateDurationCts = null;
        }

        void SetSelectedGeneration(TextureResult result)
        {
            if (!result.IsVideoClip())
            {
                m_OriginalClip = null;
                SetEnabled(false);
                return;
            }

            if (m_OriginalClip == result)
                return;

            m_OriginalClip = result;

            // Cancel any previous duration polling task
            m_UpdateDurationCts?.Cancel();
            m_UpdateDurationCts?.Dispose();
            m_UpdateDurationCts = new CancellationTokenSource();

            // Reset trim times in the state for the new clip
            this.Dispatch(GenerationSettingsActions.setTrimStartTime, 0f);
            this.Dispatch(GenerationSettingsActions.setTrimEndTime, 1f);

            // Get duration from the preview player (which has probed the actual video)
            _ = UpdateDurationFromPreviewAsync(m_UpdateDurationCts.Token);
        }

        async Task UpdateDurationFromPreviewAsync(CancellationToken token)
        {
            // Wait a bit for the preview to load the video
            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (token.IsCancellationRequested)
                    return;

                if (m_Preview.duration > 0.05)
                {
                    m_OriginalClipLength = m_Preview.duration;
                    SetEnabled(true);
                    UpdatePreviewPlaybackRange();
                    return;
                }
                await EditorTask.Delay(100, token);
            }

            if (token.IsCancellationRequested)
                return;

            // Timeout or no valid duration - disable
            SetEnabled(false);
        }

        void UpdateStartTime(float time)
        {
            if (Mathf.Approximately(m_StartTimeNormalized, time))
                return;
            m_StartTimeNormalized = time;
            UpdatePreviewPlaybackRange();
        }

        void UpdateEndTime(float time)
        {
            if (Mathf.Approximately(m_EndTimeNormalized, time))
                return;
            m_EndTimeNormalized = time;
            UpdatePreviewPlaybackRange();
        }

        void UpdatePreviewPlaybackRange()
        {
            if (m_OriginalClip == null)
                return;

            var startTimeSeconds = (float)(m_StartTimeNormalized * m_OriginalClipLength);
            var endTimeSeconds = (float)(m_EndTimeNormalized * m_OriginalClipLength);

            // This ensures the Double Buffer player loops between these two points
            m_Preview.SetPlaybackRange(startTimeSeconds, endTimeSeconds);

            UpdateSliderControls();
        }

        void UpdateSliderControls()
        {
            if (m_IsUpdatingSlider)
                return;

            m_IsUpdatingSlider = true;
            try
            {
                var values = m_TimeSlider.value;
                values[k_StartTimeIndex] = m_StartTimeNormalized;
                values[k_EndTimeIndex] = m_EndTimeNormalized;
                m_TimeSlider.SetValueWithoutNotify(values);

                var startTimeInSeconds = m_StartTimeNormalized * m_OriginalClipLength;
                var endTimeInSeconds = m_EndTimeNormalized * m_OriginalClipLength;
                
                m_TimeSlider.SetControlTooltip(k_StartTimeIndex, $"Start: {startTimeInSeconds:F2}s");
                m_TimeSlider.SetControlTooltip(k_EndTimeIndex, $"End: {endTimeInSeconds:F2}s");
            }
            finally
            {
                m_IsUpdatingSlider = false;
            }
        }

        void OnControlDragStarted(int controlIndex)
        {
            if (controlIndex is not (k_StartTimeIndex or k_EndTimeIndex))
                return;
            
            m_IsDraggingSlider = true;
            // Pause while dragging so the seek is instant and not fighting playback
            m_Preview.isPlaying = false;
            m_ResumePlaybackCts?.Cancel();
        }

        void OnControlDragEnded(int controlIndex)
        {
            if (controlIndex is not (k_StartTimeIndex or k_EndTimeIndex))
                return;
            
            m_IsDraggingSlider = false;
            // Resume playback immediately on release
            m_Preview.isPlaying = true;
        }

        void OnTimeSliderValueChanged(ChangeEvent<float[]> evt)
        {
            if (m_IsUpdatingSlider)
                return;

            var newStartTime = evt.newValue[k_StartTimeIndex];
            var newEndTime = evt.newValue[k_EndTimeIndex];
            var hasChanged = false;

            // Handle Start Time Change
            if (!Mathf.Approximately(m_StartTimeNormalized, newStartTime))
            {
                this.Dispatch(GenerationSettingsActions.setTrimStartTime, newStartTime);
                m_Preview.currentTime = (float)(newStartTime * m_OriginalClipLength);
                hasChanged = true;
            }

            // Handle End Time Change
            if (!Mathf.Approximately(m_EndTimeNormalized, newEndTime))
            {
                this.Dispatch(GenerationSettingsActions.setTrimEndTime, newEndTime);
                m_Preview.currentTime = (float)(newEndTime * m_OriginalClipLength);
                hasChanged = true;
            }

            if (hasChanged && !m_IsDraggingSlider)
            {
                m_Preview.isPlaying = false;
                _ = DebouncedResumePlaybackAsync();
            }
        }

        async Task DebouncedResumePlaybackAsync()
        {
            try
            {
                m_ResumePlaybackCts?.Cancel();
                m_ResumePlaybackCts?.Dispose();
                m_ResumePlaybackCts = new CancellationTokenSource();
                var token = m_ResumePlaybackCts.Token;

                await EditorTask.Delay(k_DebounceResumePlaybackMs, token);

                if (!token.IsCancellationRequested)
                    m_Preview.isPlaying = true;
            }
            catch (TaskCanceledException)
            {
                // Task cancelled, safe to ignore
            }
        }

        void OnPreviewTimeUpdated(float currentTime)
        {
            if (m_IsUpdatingSlider || m_IsDraggingSlider || m_OriginalClipLength <= 0)
                return;

            m_IsUpdatingSlider = true;
            try
            {
                var values = m_TimeSlider.value;
                values[k_CurrentTimeIndex] = (float)(currentTime / m_OriginalClipLength);
                m_TimeSlider.SetValueWithoutNotify(values);
                m_TimeSlider.SetControlTooltip(k_CurrentTimeIndex, $"{currentTime:F2}s");
            }
            finally
            {
                m_IsUpdatingSlider = false;
            }
        }

        async Task OnApplyButtonClick()
        {
            if (m_OriginalClip == null)
                return;

            var path = TempUtilities.GetTempFileName("Trim");
            await SaveTrimmedVideoAsync(path);
        }

        async Task SaveTrimmedVideoAsync(string outputPath)
        {
            if (m_OriginalClip == null)
                return;

            var wasPlaying = m_Preview.isPlaying;
            m_Preview.isPlaying = false;

            try
            {
                var (success, length) = await m_OriginalClip.TrimAndSaveAsync(m_StartTimeNormalized, m_EndTimeNormalized, outputPath, progress => {
                    EditorUtility.DisplayProgressBar("Trimming Video", $"Processing... {(int)(progress * 100)}%", progress);
                });

                if (!success)
                    return;

                var asset = this.GetAsset();
                var tempResult = TextureResult.FromPath(outputPath);
                var metadata = TextureResultExtensions.MakeMetadata(null, asset);
                metadata.refinementMode = nameof(RefinementMode.Spritesheet);
                metadata.spriteSheet = true;
                metadata.duration = length;
                await tempResult.CopyToProject(metadata, asset.GetGeneratedAssetsPath());
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                m_Preview.isPlaying = wasPlaying;
            }
        }
    }

    static class VideoUtils
    {
        public static async Task<(bool success, float length)> TrimAndSaveAsync(this TextureResult textureResult, double startTime, double endTime, string outputPath, Action<float> progress)
        {
            var (videoClip, scope) = await textureResult.GetVideoClipWithScope();
            try
            {
                if (videoClip == null)
                    throw new Exception($"Cannot import a sprite sheet from '{textureResult.uri}'.");

                var length = endTime * videoClip.length - startTime * videoClip.length;
                if (length <= 0)
                    return (false, 0);

                using var videoStream = await videoClip.ConvertAsync(startTime * videoClip.length, endTime * videoClip.length, VideoClipExtensions.Format.MP4, deleteOutputOnClose: true, progress);
                if (videoStream == null)
                {
                    Debug.LogError("Video conversion failed. The resulting stream was null.");
                    return (false, 0);
                }

                using var fileStream = FileIO.OpenWrite(outputPath);
                await videoStream.CopyToAsync(fileStream);

                return (true, (float)length);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return (false, 0);
            }
            finally
            {
                scope?.Dispose();
            }
        }
    }
}
