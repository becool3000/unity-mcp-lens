using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Animate.Services.Stores.Actions;
using Unity.AI.Animate.Services.Stores.Selectors;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Components
{
    [UxmlElement]
    partial class AnimateLoop : VisualElement
    {
        // Bitflag enum to track which parameters have changed
        [Flags]
        enum DirtyFlags
        {
            None = 0,
            Asset = 1 << 0,
            MinimumTime = 1 << 1,
            MaximumTime = 1 << 2,
            DurationCoverage = 1 << 3,
            MotionCoverage = 1 << 4,
            MuscleTolerance = 1 << 5,
            InPlace = 1 << 6,
            UseBestLoop = 1 << 7,
            Search = Asset | MinimumTime | MaximumTime | DurationCoverage | MotionCoverage | MuscleTolerance | UseBestLoop,
            All = ~0
        }

        // Field to track dirty state
        DirtyFlags m_DirtyFlags = DirtyFlags.None;

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Animate/Components/AnimateLoop/AnimateLoop.uxml";
        const int k_DebounceMsDelay = 300;

        // MultiSlider control indices
        const int k_CurrentTimeIndex = 0;
        const int k_MinTimeIndex = 1;
        const int k_MaxTimeIndex = 2;

        // Static collection to track all active AnimateLoop components
        static readonly HashSet<AnimateLoop> k_AnimateLoopComponents = new();

        AnimationClip m_OriginalClip; // Used to notice changes
        AnimationClip m_WorkingClip; // Used for loop finding calculations
        AnimationClip m_PreviewClip; // Used for preview display

        float m_MinimumTime = 0f;
        float m_MaximumTime = 1f;
        float m_DurationCoverage = 0.25f;
        float m_MotionCoverage = 0.5f;
        float m_MuscleTolerance = 1.0f;
        bool m_InPlace = true;
        bool m_UseBestLoop = true;

        readonly LoopPreview m_Preview;
        readonly MultiSlider m_TimeSlider;
        readonly Button m_ApplyButton;
        readonly VisualElement m_Buttons;

        // UI elements moved from LoopOptions
        readonly Toggle m_RootMotion;
        readonly Toggle m_BestLoop;
        readonly Slider m_DurationCoverageSlider;
        readonly TextField m_DurationCoverageTextField;

        float m_OriginalClipLength = 1f; // Store clip length for normalization
        bool m_UpdatingSlider = false; // Prevents recursive updates

        CancellationTokenSource m_UpdateCts;
        CancellationTokenSource m_ResumePlaybackCts;
        bool m_UpdatePending;
        bool m_ProcessingUpdate;
        (bool success, float startTime, float endTime, float startTimeNormalized, float endTimeNormalized, float score) m_Result;

        (float startTime, float endTime) previewResult => m_UseBestLoop ? (m_Result.startTime, m_Result.endTime) : (m_MinimumTime * m_OriginalClipLength, m_MaximumTime * m_OriginalClipLength);

        public AnimateLoop()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            SetEnabled(false);

            this.Use(state => state.SelectReferenceClip(this), t => _ = SetClip(t));
            this.Use(state => state.SelectLoopMinimumTime(this), UpdateMinimumTime);
            this.Use(state => state.SelectLoopMaximumTime(this), UpdateMaximumTime);
            this.Use(state => state.SelectLoopDurationCoverage(this), UpdateDurationCoverage);
            this.Use(state => state.SelectLoopMotionCoverage(this), UpdateMotionCoverage);
            this.Use(state => state.SelectLoopMuscleTolerance(this), UpdateMuscleTolerance);
            this.Use(state => state.SelectLoopInPlace(this), UpdateInPlace);
            this.Use(state => state.SelectUseBestLoop(this), UpdateUseBestLoop);
            this.Use(_ => isVisible, UpdateVisibility);

            m_Buttons = this.Q<VisualElement>("ButtonsContainer");

            m_ApplyButton = this.Q<Button>("Apply");
            m_ApplyButton.clicked += () => _ = OnApplyButtonClick();

            m_Preview = this.Q<LoopPreview>();
            m_TimeSlider = this.Q<MultiSlider>();

            // Initialize controls moved from LoopOptions
            m_RootMotion = this.Q<Toggle>("loop-options-root-motion");
            m_RootMotion.RegisterValueChangedCallback(evt => {
                this.Dispatch(GenerationSettingsActions.setLoopInPlace, !evt.newValue);
            });

            m_BestLoop = this.Q<Toggle>("loop-options-best-loop");
            m_BestLoop.RegisterValueChangedCallback(evt => {
                this.Dispatch(GenerationSettingsActions.setUseBestLoop, evt.newValue);
                m_DurationCoverageSlider.SetEnabled(evt.newValue);
            });

            m_DurationCoverageSlider = this.Q<Slider>("loop-options-duration-coverage");
            m_DurationCoverageTextField = m_DurationCoverageSlider.Q<TextField>();
            m_DurationCoverageSlider.RegisterValueChangedCallback(evt => {
                var normalizedValue = Mathf.Clamp01(evt.newValue / m_OriginalClipLength);
                this.Dispatch(GenerationSettingsActions.setLoopDurationCoverage, normalizedValue);
                m_DurationCoverageTextField.SetValueWithoutNotify(evt.newValue.ToString("F2") + "s");
            });

            // Initialize toggle states from app state
            this.Use(state => state.SelectLoopInPlace(this), inPlace => m_RootMotion.value = !inPlace);

            // Initialize Best Loop toggle from state
            this.Use(state => state.SelectUseBestLoop(this), useBestLoop =>
            {
                m_BestLoop.value = useBestLoop;
                m_DurationCoverageSlider.SetEnabled(useBestLoop);
            });

            // Initialize duration coverage value from state
            this.Use(state => state.SelectLoopDurationCoverage(this), coverage => {
                m_DurationCoverageSlider.value = coverage * m_OriginalClipLength;
                m_DurationCoverageTextField.SetValueWithoutNotify((coverage * m_OriginalClipLength).ToString("F2") + "s");
            });

            m_TimeSlider.controlCount = 3;
            m_TimeSlider.draggableControls = new[] { false, true, true };
            m_TimeSlider.RegisterValueChangedCallback(OnTimeSliderValueChanged);
            m_Preview.animationTimeChanged += OnPreviewTimeUpdated;

            RegisterCallback<AttachToPanelEvent>(arg => {
                k_AnimateLoopComponents.Add(this);
                m_DirtyFlags |= DirtyFlags.Asset;
                _ = DebounceUpdateLoopPreviewAsync();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => k_AnimateLoopComponents.Remove(this));
        }

        async Task SetClip(Task<AnimationClip> clip)
        {
            var originalClip = await clip;
            var originalClipLength = originalClip ? originalClip.length : 0.0001f;
            SetEnabled(originalClip && originalClipLength > 0.05f);

            if (m_OriginalClip == originalClip && Mathf.Approximately(m_OriginalClipLength, originalClipLength))
                return;

            m_OriginalClip = originalClip;
            m_OriginalClipLength = originalClipLength;

            this.GetStoreApi().Dispatch(GenerationSettingsActions.setLoopMinimumTime, 0);
            this.GetStoreApi().Dispatch(GenerationSettingsActions.setLoopMaximumTime, 1);
            this.GetStoreApi().Dispatch(GenerationSettingsActions.setLoopDurationCoverage, 0.25f);

            m_Buttons.SetEnabled(false);

            CreateWorkingCopies(originalClip);
            m_Preview.animationClip = m_PreviewClip;

            UpdateTimeSlider();

            // Update the duration coverage slider with new clip length
            UpdateDurationCoverageRange();

            m_DirtyFlags |= DirtyFlags.Asset;
            _ = DebounceUpdateLoopPreviewAsync();
        }

        // Method moved from LoopOptions to update the duration coverage slider's range
        void UpdateDurationCoverageRange()
        {
            if (m_DurationCoverageSlider == null || m_OriginalClipLength <= 0f)
                return;

            // Minimum possible duration is small but not zero
            var minPossibleDuration = 0.01f * m_OriginalClipLength;

            // Maximum possible duration is the delta between min and max time
            var maxPossibleDuration = m_MaximumTime * m_OriginalClipLength - m_MinimumTime * m_OriginalClipLength;

            // Ensure we have a valid range
            if (maxPossibleDuration < minPossibleDuration)
                maxPossibleDuration = minPossibleDuration;

            m_DurationCoverageSlider.lowValue = minPossibleDuration;
            m_DurationCoverageSlider.highValue = maxPossibleDuration;

            // Make sure current value is within new range
            if (m_DurationCoverageSlider.value > maxPossibleDuration)
            {
                m_DurationCoverageSlider.value = maxPossibleDuration;
                var normalizedValue = Mathf.Clamp01(maxPossibleDuration / m_OriginalClipLength);
                this.Dispatch(GenerationSettingsActions.setLoopDurationCoverage, normalizedValue);
                m_DurationCoverageTextField.SetValueWithoutNotify(maxPossibleDuration.ToString("F2") + "s");
            }
        }

        void UpdateTimeSlider()
        {
            if (m_TimeSlider == null || m_OriginalClipLength <= 0f)
                return;

            m_UpdatingSlider = true;

            try
            {
                // Convert times to normalized values for the slider
                var normalizedValues = new float[3];
                normalizedValues[k_MinTimeIndex] = m_MinimumTime;
                normalizedValues[k_CurrentTimeIndex] = (previewResult.startTime + m_Preview.currentTime) / m_OriginalClipLength;
                normalizedValues[k_MaxTimeIndex] = m_MaximumTime;

                m_TimeSlider.SetValueWithoutNotify(normalizedValues);

                // Update tooltips with time values in seconds
                UpdateSliderTooltips();
            }
            finally
            {
                m_UpdatingSlider = false;
            }
        }

        void UpdateSliderTooltips()
        {
            // Format the tooltip text with time in seconds (2 decimal places)
            var startTimeText = $"Start: {(m_MinimumTime * m_OriginalClipLength):F2} sec";
            var endTimeText = $"End: {(m_MaximumTime * m_OriginalClipLength):F2} sec";

            // Set tooltips on min and max controls
            m_TimeSlider.SetControlTooltip(k_MinTimeIndex, startTimeText);
            m_TimeSlider.SetControlTooltip(k_MaxTimeIndex, endTimeText);
        }

        void OnTimeSliderValueChanged(ChangeEvent<float[]> evt)
        {
            if (m_UpdatingSlider || m_OriginalClipLength <= 0f)
                return;

            var values = evt.newValue;

            // Update minimum time if changed
            var newMinTime = values[k_MinTimeIndex];
            if (!Mathf.Approximately(newMinTime, m_MinimumTime))
            {
                // Store normalized value (0-1) in settings
                this.Dispatch(GenerationSettingsActions.setLoopMinimumTime, values[k_MinTimeIndex]);
                UpdateDurationCoverageRange();
            }

            // Update current time if changed
            var newCurrentTime = (values[k_CurrentTimeIndex] * m_OriginalClipLength) - previewResult.startTime;
            if (!Mathf.Approximately(newCurrentTime, m_Preview.currentTime))
            {
                m_Preview.currentTime = newCurrentTime;
            }

            // Update maximum time if changed
            var newMaxTime = values[k_MaxTimeIndex];
            if (!Mathf.Approximately(newMaxTime, m_MaximumTime))
            {
                // Store normalized value (0-1) in settings
                this.Dispatch(GenerationSettingsActions.setLoopMaximumTime, values[k_MaxTimeIndex]);
                UpdateDurationCoverageRange();
            }

            // Update tooltips when values change
            UpdateSliderTooltips();
        }

        void OnPreviewTimeUpdated(float time)
        {
            if (m_OriginalClipLength <= 0f)
                return;

            m_UpdatingSlider = true;

            try
            {
                var normalizedValues = m_TimeSlider.value;
                normalizedValues[k_CurrentTimeIndex] = (previewResult.startTime + time) / m_OriginalClipLength;
                m_TimeSlider.SetValueWithoutNotify(normalizedValues);
            }
            finally
            {
                m_UpdatingSlider = false;
            }
        }

        void CreateWorkingCopies(AnimationClip originalClip)
        {
            if (originalClip == null)
                return;

            // Create or reset working clip for loop calculations
            if (m_WorkingClip == null)
                m_WorkingClip = new AnimationClip();
            EditorUtility.CopySerialized(originalClip, m_WorkingClip);
            m_WorkingClip.SetDefaultClipSettings(false);
            m_WorkingClip.name = $"{originalClip.name}_working";

            // Create or reset preview clip for display
            if (m_PreviewClip == null)
                m_PreviewClip = new AnimationClip();
            EditorUtility.CopySerialized(originalClip, m_PreviewClip);
            m_PreviewClip.SetDefaultClipSettings(false);
            m_PreviewClip.name = $"{originalClip.name}_preview";
        }

        async Task DebounceUpdateLoopPreviewAsync()
        {
            m_UpdateCts?.Cancel();
            m_UpdateCts?.Dispose();
            m_UpdateCts = new CancellationTokenSource();

            var token = m_UpdateCts.Token;
            m_UpdatePending = true;
            m_Buttons.SetEnabled(!m_UpdatePending);

            try
            {
                await EditorTask.Delay(k_DebounceMsDelay, token);

                if (token.IsCancellationRequested || !m_UpdatePending || m_ProcessingUpdate)
                    return;

                m_UpdatePending = false;
                await UpdateLoopPreviewAsync();
            }
            catch (TaskCanceledException)
            {
                // ignored
            }
            finally
            {
                m_Buttons.SetEnabled(!m_UpdatePending);
            }
        }

        bool isVisible => resolvedStyle.width is not (<= 0 or float.NaN);

        async Task UpdateLoopPreviewAsync()
        {
            if (m_OriginalClip == null || m_WorkingClip == null || m_PreviewClip == null)
                return;

            if (m_DirtyFlags == DirtyFlags.None)
                return;

            if (!isVisible)
                return;

            m_ProcessingUpdate = true;

            // Only show spinner for blocking operations when using best loop
            if (m_UseBestLoop)
                m_Preview.StartSpinner();

            try
            {
                // Only pause playback if we're using best loop (blocking UI)
                m_Preview.isPlaying = !m_UseBestLoop;

                // Recreate working copies to ensure we start with fresh clips
                CreateWorkingCopies(m_OriginalClip);

                // Always prepare working clip for loop finding with root motion flattened
                m_WorkingClip.FlattenRootMotion();

                if ((m_DirtyFlags & DirtyFlags.Search) != 0)
                {
                    if (m_UseBestLoop)
                    {
                        // Blocking search when using best loop - waits for result
                        m_Result = await m_WorkingClip.TryFindOptimalLoopPointsAsync(
                            m_MinimumTime, m_MaximumTime, m_DurationCoverage, m_MotionCoverage, m_MuscleTolerance,
                            progress =>
                            {
                                if (progress is >= 0 and <= 1)
                                    m_Preview.ShowProgress(progress);
                            });

                        m_DirtyFlags = 0;
                    }
                    else
                    {
                        try
                        {
                            // Score in background
                            var score = m_WorkingClip.ScoreLoopQuality(m_MinimumTime, m_MaximumTime);

                            m_Result = (true, m_MinimumTime * m_OriginalClipLength, m_MaximumTime * m_OriginalClipLength, m_MinimumTime, m_MaximumTime, score);

                            // Update highlight in UI after search completes
                            m_TimeSlider.highlightStart = m_Result.startTimeNormalized;
                            m_TimeSlider.highlightEnd = m_Result.endTimeNormalized;
                            UpdateHighlightColor(m_Result.score);
                        }
                        catch (Exception)
                        {
                            // Ignore exceptions in scoring
                        }
                    }
                }
                else if (!m_Result.success)
                {
                    // Use manual settings if no successful search result exists
                    m_Result = (false, m_MinimumTime * m_OriginalClipLength, m_MaximumTime * m_OriginalClipLength, m_MinimumTime, m_MaximumTime, 0);
                }

                m_DirtyFlags = 0;

                // Prepare preview clip based on in-place setting
                EditorUtility.CopySerialized(m_OriginalClip, m_PreviewClip);
                m_PreviewClip.SetDefaultClipSettings(false);

                // Always crop the preview clip to the loop points
                m_PreviewClip.Crop(previewResult.startTime, previewResult.endTime);
                m_PreviewClip.NormalizeRootTransform();

                // Only flatten root motion if in-place is enabled
                if (m_InPlace)
                    m_PreviewClip.FlattenRootMotion();

                m_Preview.animationClip = m_PreviewClip;

                // Always update highlightStart/End with current result
                m_TimeSlider.highlightStart = m_Result.startTimeNormalized;
                m_TimeSlider.highlightEnd = m_Result.endTimeNormalized;

                // Set highlight color based on score
                UpdateHighlightColor(m_Result.score);
            }
            finally
            {
                m_ProcessingUpdate = false;
                m_Preview.StopSpinner();

                // Always ensure playback is enabled after processing
                if (!m_Preview.isPlaying)
                    m_Preview.isPlaying = true;
            }
        }

        void UpdateHighlightColor(float score)
        {
            var highlightColor = score switch
            {
                >= 0.8f => Color.green,
                >= 0.65f => Color.yellow,
                >= 0.5f => new Color(1f, 0.5f, 0f),
                _ => Color.red
            };

            m_TimeSlider.highlightColor = highlightColor;
        }

        async Task OnApplyButtonClick()
        {
            var originalClip = this.GetAsset().GetObject<AnimationClip>();
            if (originalClip == null || m_PreviewClip == null)
                return;

            using var temporaryAssets = AnimationClipDatabaseUtils.ImportAssets(new[] { m_PreviewClip });
            var animationClip = temporaryAssets.assets[0].asset.GetObject<AnimationClip>();
            // we do not want to inherit the asset settings, especially not loop pose as it was off during the trimming and could change the behavior dramatically
            // if turned on so we do not copy any from the asset. The user will decide what to enable.
            animationClip.SafeCall(AssetDatabase.SaveAssetIfDirty);
            var result = AnimationClipResult.FromPath(temporaryAssets.assets[0].asset.GetPath());

            var generativePath = this.GetAsset().GetGeneratedAssetsPath();
            await result.CopyToProject(new GenerationMetadata{ isTrimmed = true }, generativePath);
        }

        void UpdateMinimumTime(float time)
        {
            m_MinimumTime = time;
            UpdateTimeSlider();
            m_DirtyFlags |= DirtyFlags.MinimumTime;
            UpdateDurationCoverageRange();
            _ = DebounceUpdateLoopPreviewAsync();
        }

        void UpdateMaximumTime(float time)
        {
            m_MaximumTime = time;
            UpdateTimeSlider();
            m_DirtyFlags |= DirtyFlags.MaximumTime;
            UpdateDurationCoverageRange();
            _ = DebounceUpdateLoopPreviewAsync();
        }

        void UpdateDurationCoverage(float coverage)
        {
            m_DurationCoverage = coverage;
            m_DirtyFlags |= DirtyFlags.DurationCoverage;
            _ = DebounceUpdateLoopPreviewAsync();
        }

        void UpdateMotionCoverage(float coverage)
        {
            m_MotionCoverage = coverage;
            m_DirtyFlags |= DirtyFlags.MotionCoverage;
            _ = DebounceUpdateLoopPreviewAsync();
        }

        void UpdateMuscleTolerance(float tolerance)
        {
            m_MuscleTolerance = tolerance;
            m_DirtyFlags |= DirtyFlags.MuscleTolerance;
            _ = DebounceUpdateLoopPreviewAsync();
        }

        void UpdateInPlace(bool inPlace)
        {
            m_InPlace = inPlace;
            m_DirtyFlags |= DirtyFlags.InPlace;
            _ = DebounceUpdateLoopPreviewAsync();
        }

        void UpdateUseBestLoop(bool useBestLoop)
        {
            m_UseBestLoop = useBestLoop;
            m_DirtyFlags |= DirtyFlags.UseBestLoop;
            _ = DebounceUpdateLoopPreviewAsync();
        }

        void UpdateVisibility(bool visible)
        {
            if (!visible)
                return;

            m_DirtyFlags |= DirtyFlags.All;
            _ = DebounceUpdateLoopPreviewAsync();
        }

        // Asset reimport monitor to detect external changes
        class AssetReferenceReimportMonitor : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                foreach (var component in k_AnimateLoopComponents)
                {
                    if (component == null || !component.GetAsset().IsValid())
                        continue;

                    foreach (var path in importedAssets)
                    {
                        if (string.IsNullOrEmpty(path))
                            continue;

                        if (component.GetAsset().GetPath() != path)
                            continue;

                        var state = component.GetState();
                        if (state == null)
                            continue;

                        // Asset was modified externally, reload it as if it was reopened
                        _ = component.SetClip(component.GetState().SelectReferenceClip(component));
                    }
                }
            }
        }
    }
}
