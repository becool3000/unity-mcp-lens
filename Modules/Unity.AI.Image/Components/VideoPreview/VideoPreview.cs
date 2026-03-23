using System;
using System.Threading.Tasks;
using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class VideoPreview : VisualElement
    {
        public event Action<float> CurrentTimeChanged;

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/VideoPreview/VideoPreview.uxml";
        const int k_LoadTimeoutMs = 5000;

        readonly VisualElement m_Image;
        readonly SpinnerManipulator m_SpinnerManipulator;

        TextureResult m_TextureResult;
        VideoPreviewPlayer m_PreviewPlayer;

        float m_CurrentTime;
        float m_PlaybackStartTime;
        float m_PlaybackEndTime = float.MaxValue;
        double m_LastEditorTime;
        bool m_IsPlaying;
        
        bool m_WasPlayingBeforeFocusLost;

        IVisualElementScheduledItem m_UpdateScheduler;

        public VideoPreview()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Image = this.Q<VisualElement>("border");
            var progress = this.Q<VisualElement>("progress");
            progress.AddManipulator(m_SpinnerManipulator = new SpinnerManipulator());

            RegisterCallback<GeometryChangedEvent>(e => _ = UpdateFrame());
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            this.UseContext<ScreenScaleFactor>(OnScreenScaleFactorChanged, false);

            isPlaying = true;

            this.Use(state => state.SelectSelectedGeneration(this), SetSelectedGeneration);
        }

        void StartSpinner()
        {
            m_SpinnerManipulator.Start();
            m_Image.style.opacity = 0.5f;
        }

        void StopSpinner()
        {
            m_SpinnerManipulator.Stop();
            m_Image.style.opacity = 1.0f;
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            _ = SetSourceAsync(m_TextureResult, forceReload: true);
            m_UpdateScheduler?.Resume();
            EditorApplication.focusChanged += OnApplicationFocusChanged;
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            EditorApplication.focusChanged -= OnApplicationFocusChanged;
            m_PreviewPlayer?.Dispose();
            m_PreviewPlayer = null;
            m_UpdateScheduler?.Pause();
        }
        
        void OnApplicationFocusChanged(bool hasFocus)
        {
            if (hasFocus)
            {
                if (m_WasPlayingBeforeFocusLost) 
                    isPlaying = true;
            }
            else
            {
                m_WasPlayingBeforeFocusLost = m_IsPlaying;
                isPlaying = false;
            }
        }

        void OnScreenScaleFactorChanged(ScreenScaleFactor context) => _ = UpdateFrame();

        void UpdatePlaybackTime()
        {
            if (!m_IsPlaying || panel == null || m_PreviewPlayer == null || !m_PreviewPlayer.isReady)
                return;

            var currentEditorTime = EditorApplication.timeSinceStartup;
            var deltaTime = currentEditorTime - m_LastEditorTime;
            m_LastEditorTime = currentEditorTime;

            var clipDuration = m_PreviewPlayer.duration;
            var effectiveEndTime = m_PlaybackEndTime > 0.001f ? Mathf.Min(m_PlaybackEndTime, (float)clipDuration) : (float)clipDuration;
            var effectiveStartTime = Mathf.Clamp(m_PlaybackStartTime, 0, effectiveEndTime);

            // First playback after domain reload: limit duration to speed up warmup loop
            // The decoder needs one Seek cycle before sequential GetNextFrame works
            var needsWarmup = VideoPreviewPlayer.needsWarmupLoop;
            if (needsWarmup)
                effectiveEndTime = Mathf.Min(effectiveEndTime, 0.1f);

            // Guard against zero/invalid time ranges (e.g., trim start at or above end time)
            var timeRange = effectiveEndTime - effectiveStartTime;
            if (timeRange <= 0)
            {
                // Invalid range - just stay at start time
                currentTime = effectiveStartTime;
                return;
            }

            var newTime = m_CurrentTime + (float)deltaTime;

            // 1. Check for Loop (Past End Time) - requires seek
            if (newTime >= effectiveEndTime)
            {
                // Mark warmup complete after first loop
                if (needsWarmup)
                    VideoPreviewPlayer.MarkWarmupComplete();

                var overflow = newTime - effectiveEndTime;
                newTime = effectiveStartTime + (overflow % timeRange);
                m_PreviewPlayer.Seek(newTime);
                currentTime = newTime;
                return;
            }

            // 2. Check for Out of Bounds (Before Start Time) - requires seek
            if (newTime < effectiveStartTime)
            {
                newTime = effectiveStartTime;
                m_PreviewPlayer.Seek(newTime);
                currentTime = newTime;
                return;
            }

            // 3. Normal forward playback - use sequential frame advancement (fast)
            // Decode frames up to target time, but keep m_CurrentTime based on real elapsed time
            m_PreviewPlayer.AdvanceToTime(newTime);
            m_CurrentTime = newTime;
            CurrentTimeChanged?.Invoke(m_CurrentTime);
        }

        async Task UpdateFrame()
        {
            if (panel == null)
                return;

            var width = resolvedStyle.width;
            var height = resolvedStyle.height;

            if (width is <= 0 or float.NaN || height is <= 0 or float.NaN)
                return;

            var screenScaleFactor = this.GetContext<ScreenScaleFactor>()?.value ?? 1.0f;

            RenderTexture frameTexture = null;
            if (m_PreviewPlayer != null && m_PreviewPlayer.isReady)
            {
                frameTexture = m_PreviewPlayer.outputTexture;
                // Note: Drift correction removed - AdvanceToTime keeps player in sync with m_CurrentTime
            }
            
            if (!frameTexture.IsValid() && m_TextureResult.IsValid())
            {
                frameTexture = await TextureCache.GetPreview(m_TextureResult.uri, (int)(width * screenScaleFactor));
            }

            if (frameTexture != null)
            {
                m_Image.style.backgroundImage = Background.FromRenderTexture(frameTexture);
                m_Image.MarkDirtyRepaint();
                m_Image.EnableInClassList("image-scale-to-fit", resolvedStyle.width <= frameTexture.width || resolvedStyle.height <= frameTexture.height);
                m_Image.EnableInClassList("image-scale-initial", resolvedStyle.width > frameTexture.width && resolvedStyle.height > frameTexture.height);
            }
        }

        public void SetPlaybackRange(float startTime, float endTime)
        {
            var startChanged = !Mathf.Approximately(m_PlaybackStartTime, startTime);
            var endChanged = !Mathf.Approximately(m_PlaybackEndTime, endTime);

            m_PlaybackStartTime = startTime;
            m_PlaybackEndTime = endTime;

            if (m_PreviewPlayer == null || !m_PreviewPlayer.isReady)
                return;

            // Determine seek target based on which marker changed
            float seekTarget;
            if (startChanged && !endChanged)
            {
                // Start marker was dragged - seek to show start position
                seekTarget = startTime;
            }
            else if (endChanged && !startChanged)
            {
                // End marker was dragged - seek to show end position
                seekTarget = endTime;
            }
            else if (m_CurrentTime < startTime)
            {
                // Current time before range - clamp to start
                seekTarget = startTime;
            }
            else if (m_CurrentTime > endTime)
            {
                // Current time after range - clamp to end
                seekTarget = endTime;
            }
            else
            {
                // Both changed or current time is valid - no seek needed
                return;
            }

            currentTime = seekTarget;
            m_PreviewPlayer.Seek(seekTarget);
        }

        public bool isPlaying
        {
            get => m_IsPlaying;
            set
            {
                if (m_IsPlaying == value)
                    return;
                m_IsPlaying = value;

                if (m_IsPlaying)
                {
                    m_LastEditorTime = EditorApplication.timeSinceStartup;
                    if (m_UpdateScheduler == null)
                        m_UpdateScheduler = schedule.Execute(UpdatePlaybackTime).Every(40);
                    else
                        m_UpdateScheduler.Resume();
                }
                else
                {
                    m_UpdateScheduler?.Pause();
                }
            }
        }

        public double duration => m_PreviewPlayer?.isReady == true ? m_PreviewPlayer.duration : 0;

        void SetSelectedGeneration(TextureResult result) => _ = SetSourceAsync(result);

        async Task SetSourceAsync(TextureResult result, bool forceReload = false)
        {
            if (m_TextureResult == result && !forceReload)
                return;

            m_PreviewPlayer?.Dispose();
            m_PreviewPlayer = null;
            m_TextureResult = result;
            m_Image.style.backgroundImage = null;

            if (result == null || !result.IsVideoClip())
                return;

            VideoPreviewPlayer newPlayer = null;
            Action onReady = null;

            try
            {
                StartSpinner();
                await UpdateFrame(); 

                var filePath = result.uri.GetLocalPath();
                newPlayer = new VideoPreviewPlayer(filePath, 256, 256);
                
                var readyTcs = new TaskCompletionSource<bool>();
                onReady = () => readyTcs.TrySetResult(true);

                newPlayer.OnReady += onReady;
                
                // Manually trigger init and check for immediate failure
                if (!await newPlayer.InitializeAsync())
                {
                    newPlayer.Dispose();
                    Debug.LogWarning($"[VideoPreview] Failed to initialize player for {result.uri}");
                    return;
                }

                // Add timeout to prevent infinite spinner if Prepare fails silently
                var timeoutTask = EditorTask.Delay(k_LoadTimeoutMs);
                var completedTask = await Task.WhenAny(readyTcs.Task, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    newPlayer.Dispose();
                    Debug.LogWarning($"[VideoPreview] Load timeout for {result.uri}");
                    return; 
                }
                
                // Propagate any exceptions from the ready task
                await readyTcs.Task; 

                if (m_TextureResult != result)
                {
                    newPlayer.Dispose();
                    return;
                }
                
                if (m_PlaybackEndTime <= 0 || m_PlaybackEndTime > newPlayer.duration)
                    m_PlaybackEndTime = (float)newPlayer.duration;

                m_PreviewPlayer = newPlayer;
                m_LastEditorTime = EditorApplication.timeSinceStartup;

                currentTime = m_PlaybackStartTime;
                // Only seek if start time is non-zero; Initialize() already positioned at frame 0
                if (m_PlaybackStartTime > 0.001f)
                    m_PreviewPlayer.Seek(m_PlaybackStartTime);
                m_PreviewPlayer.Play();
            }
            catch (Exception e)
            {
                Debug.LogError($"[VideoPreview] Error loading {result?.uri}: {e.Message}");
                newPlayer?.Dispose();
                if (m_PreviewPlayer == newPlayer) m_PreviewPlayer = null;
            }
            finally
            {
                // Unsubscribe from the local player instance to avoid leaks
                if (newPlayer != null)
                {
                    newPlayer.OnReady -= onReady;
                }

                // If assignment failed, dispose
                if (m_PreviewPlayer != newPlayer)
                {
                    newPlayer?.Dispose();
                }

                StopSpinner();
            }
        }

        public float currentTime
        {
            get => m_CurrentTime;
            set
            {
                if (Mathf.Approximately(m_CurrentTime, value))
                    return;
                m_CurrentTime = value;
                CurrentTimeChanged?.Invoke(m_CurrentTime);
                _ = UpdateFrame();
            }
        }
    }
}
