using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity.AI.Animate.Services.Utilities
{
    static class AnimationClipLoopUtils
    {
        const float k_MinLoopDurationSeconds = 0.05f;
        const float k_DurationReductionStepPercent = 0.02f;
        const float k_SamplesPerSecond = 15f;
        const int k_AmplitudeSampleCount = 30;

        const float k_MajorBoneRotationWeight = 2.5f;
        const float k_VelocityMatchWeight = 1.5f;
        const float k_RootVerticalWeight = 3.0f;

        const float k_ExcellentLoopScore = 0.05f;

        class ClipCacheEntry
        {
            public readonly Dictionary<EditorCurveBinding, AnimationCurve> curveCache = new();
            public readonly Dictionary<(EditorCurveBinding, float), float> sampleCache = new();
            public float[] timePoints;
            public float clipLength;
        }

        static readonly Dictionary<AnimationClip, ClipCacheEntry> k_ClipCache = new();
        static readonly LinkedList<AnimationClip> k_LruList = new();

        const int k_MaxCachedClips = 20;

        /// <summary>
        /// Crops an AnimationClip in-place by rebuilding its curves to fit within the specified time range.
        /// This is a destructive operation intended for use on dynamically created clips.
        /// </summary>
        public static void Crop(this AnimationClip clip, float startTime, float endTime)
        {
            if (clip == null || endTime <= startTime)
                return;

            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.keys.Length == 0)
                    continue;

                var newKeys = new List<Keyframe>();
                foreach (var key in curve.keys)
                {
                    if (key.time < startTime || key.time > endTime)
                        continue;

                    var newKey = new Keyframe(key.time - startTime, key.value, key.inTangent, key.outTangent, key.inWeight, key.outWeight);
                    newKeys.Add(newKey);
                }

                var newCurve = new AnimationCurve(newKeys.ToArray());
                AnimationUtility.SetEditorCurve(clip, binding, newCurve);
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0;
            settings.stopTime = endTime - startTime;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
        }

        /// <summary>
        /// Evaluates the looping quality of a specific range in an animation clip.
        /// </summary>
        /// <param name="clip">The source animation clip.</param>
        /// <param name="searchWindowStart">Normalized start time (0-1) for the loop range to evaluate.</param>
        /// <param name="searchWindowEnd">Normalized end time (0-1) for the loop range to evaluate.</param>
        /// <returns>A score between 0 and 1 indicating loop quality, where 1 is a perfect loop.</returns>
        public static float ScoreLoopQuality(this AnimationClip clip, float searchWindowStart = 0f, float searchWindowEnd = 1f)
        {
            if (clip == null)
                return 0f;

            searchWindowStart = Mathf.Clamp01(searchWindowStart);
            searchWindowEnd = Mathf.Clamp01(searchWindowEnd);

            // Convert normalized times to seconds
            var startTimeSeconds = searchWindowStart * clip.length;
            var endTimeSeconds = searchWindowEnd * clip.length;

            if (endTimeSeconds - startTimeSeconds < k_MinLoopDurationSeconds ||
                Mathf.Approximately(startTimeSeconds, endTimeSeconds))
                return 0f;

            var cacheEntry = GetOrCreateClipCache(clip);
            var relevantBindings = new List<EditorCurveBinding>();
            var amplitudeCheckBindings = new List<EditorCurveBinding>();

            // Get relevant bindings from cache or build them
            if (cacheEntry.curveCache.Count != 0)
            {
                foreach (var binding in cacheEntry.curveCache.Keys)
                {
                    relevantBindings.Add(binding);
                    if (IsAmplitudeTracked(binding))
                    {
                        amplitudeCheckBindings.Add(binding);
                    }
                }
            }
            else
            {
                var allCurveBindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var binding in allCurveBindings)
                {
                    if (binding.type != typeof(Animator))
                        continue;

                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.keys.Length <= 1)
                        continue;

                    relevantBindings.Add(binding);
                    cacheEntry.curveCache[binding] = curve;

                    if (IsAmplitudeTracked(binding))
                        amplitudeCheckBindings.Add(binding);
                }
            }

            if (relevantBindings.Count == 0)
                return 0f;

            // Evaluate the actual loop quality
            var totalOriginalAmplitude = CalculateMotionAmplitude(amplitudeCheckBindings, cacheEntry.curveCache, 0f, clip.length);
            var (score, _) = EvaluateLoopSimilarity(relevantBindings, cacheEntry.curveCache, cacheEntry.sampleCache,
                startTimeSeconds, endTimeSeconds, 1f / 60f, amplitudeCheckBindings, totalOriginalAmplitude);

            // Convert to a 0-1 score where 1 is perfect
            return 1f - Mathf.Clamp01(score);
        }

        /// <summary>
        /// Asynchronously attempts to find an optimal, high-quality loop within an animation clip.
        /// </summary>
        /// <param name="clip">The source animation clip.</param>
        /// <param name="searchWindowStart">Desired normalized start time (0-1) for the loop. Default is 0 (beginning of clip).</param>
        /// <param name="searchWindowEnd">Desired normalized end time (0-1) for the loop. Default is 1 (end of clip).</param>
        /// <param name="minimumWindowSize">Minimum loop duration as a percentage of the clip's length.</param>
        /// <param name="minimumMotionCoverage">Minimum acceptable ratio of motion amplitude compared to the original clip.</param>
        /// <param name="muscleMatchingTolerance">The highest (worst) score that is still considered an acceptable loop. A lower value is stricter.</param>
        /// <param name="progressCallback">Optional callback for tracking progress (0-1) of the algorithm.</param>
        /// <returns>A task containing a tuple with the results: (success, startTime, endTime, startTimeNormalized, endTimeNormalized, score)</returns>
        public static async Task<(bool success, float startTime, float endTime, float startTimeNormalized, float endTimeNormalized, float score)> TryFindOptimalLoopPointsAsync(this AnimationClip clip,
            float searchWindowStart = 0f, float searchWindowEnd = 1f, float minimumWindowSize = 0.25f,
            float minimumMotionCoverage = 0.25f, float muscleMatchingTolerance = 1.0f,
            Action<float> progressCallback = null)
        {
            var startTime = 0f;
            var endTime = clip?.length ?? 0f;
            var normalizedScore = 0f;

            if (clip == null)
                return (false, startTime, endTime, 0f, 1f, normalizedScore);

            searchWindowStart = Mathf.Clamp01(searchWindowStart);
            searchWindowEnd = Mathf.Clamp01(searchWindowEnd);

            var minDurationFromRatio = clip.length * minimumWindowSize;
            var minLoopDuration = Mathf.Max(k_MinLoopDurationSeconds, minDurationFromRatio);

            var desiredStartTimeSeconds = searchWindowStart * clip.length;
            var desiredEndTimeSeconds = searchWindowEnd * clip.length;

            if (desiredEndTimeSeconds - desiredStartTimeSeconds < minLoopDuration)
            {
                desiredEndTimeSeconds = desiredStartTimeSeconds + minLoopDuration;

                if (desiredEndTimeSeconds > clip.length)
                {
                    desiredEndTimeSeconds = clip.length;
                    desiredStartTimeSeconds = Mathf.Max(0f, desiredEndTimeSeconds - minLoopDuration);
                }
            }

            if (clip.length < minLoopDuration)
                return (false, startTime, endTime, 0f, 1f, normalizedScore);

            var cacheEntry = GetOrCreateClipCache(clip);
            var relevantBindings = new List<EditorCurveBinding>();
            var amplitudeCheckBindings = new List<EditorCurveBinding>();

            // Report initial progress
            progressCallback?.Invoke(0.05f);

            // First yield point - after setting up initial data structures
            await EditorTask.Yield();

            if (cacheEntry.curveCache.Count != 0)
            {
                foreach (var binding in cacheEntry.curveCache.Keys)
                {
                    relevantBindings.Add(binding);
                    if (IsAmplitudeTracked(binding))
                    {
                        amplitudeCheckBindings.Add(binding);
                    }
                }
            }
            else
            {
                var allCurveBindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var binding in allCurveBindings)
                {
                    if (binding.type != typeof(Animator))
                        continue;

                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.keys.Length <= 1)
                        continue;

                    relevantBindings.Add(binding);
                    cacheEntry.curveCache[binding] = curve;

                    if (IsAmplitudeTracked(binding))
                        amplitudeCheckBindings.Add(binding);
                }
            }

            if (relevantBindings.Count == 0)
                return (false, startTime, endTime, 0f, 1f, normalizedScore);

            // Second yield point - after collecting curve data
            await EditorTask.Yield();
            progressCallback?.Invoke(0.1f);

            var totalOriginalAmplitude = CalculateMotionAmplitude(amplitudeCheckBindings, cacheEntry.curveCache, 0f, clip.length);

            var globalBestScore = float.MaxValue;
            var globalBestStart = 0f;
            var globalBestEnd = 0f;
            var globalBestMotionCoverageRatio = 0f;

            var durationReductionPerAttempt = clip.length * k_DurationReductionStepPercent;
            const float velocityDelta = 1f / 60f;

            // Find the indices in timePoints that correspond to our search window
            var startTimeIndex = FindClosestTimePointIndex(cacheEntry.timePoints, desiredStartTimeSeconds);
            var endTimeIndex = FindClosestTimePointIndex(cacheEntry.timePoints, desiredEndTimeSeconds);

            // Estimate total iterations for progress tracking
            var initialDuration = desiredEndTimeSeconds - desiredStartTimeSeconds;
            var durationStepCount = Mathf.CeilToInt((initialDuration - minLoopDuration) / durationReductionPerAttempt) + 1;
            var maxStartIndices = endTimeIndex - startTimeIndex;
            var currentIteration = 0;

            var loopCount = 0;
            var currentDurationStep = 0;
            for (var targetDuration = desiredEndTimeSeconds - desiredStartTimeSeconds; targetDuration >= minLoopDuration; targetDuration -= durationReductionPerAttempt)
            {
                currentDurationStep++;

                // Add a yield every few iterations to keep the UI responsive
                if (++loopCount % 3 == 0)
                {
                    await EditorTask.Yield();
                    // Report progress at outer loop level
                    progressCallback?.Invoke(0.1f + 0.8f * (float)currentDurationStep / durationStepCount);
                }

                var attemptBestMotionCoverageRatio = 0f;
                var attemptBestScore = float.MaxValue;
                var attemptBestStart = desiredStartTimeSeconds;
                var maxStartTime = desiredEndTimeSeconds - targetDuration;

                maxStartTime = Mathf.Min(maxStartTime, desiredStartTimeSeconds + (desiredEndTimeSeconds - desiredStartTimeSeconds - minLoopDuration));

                // Find the indices in timePoints that correspond to our current search range
                var currentStartIndex = startTimeIndex;
                var maxStartIndex = FindClosestTimePointIndex(cacheEntry.timePoints, maxStartTime);

                var innerLoopCount = 0;
                var currentInnerStepCount = 0;
                for (var startIndex = currentStartIndex; startIndex <= maxStartIndex; startIndex++)
                {
                    currentInnerStepCount++;
                    currentIteration++;

                    // Add a yield every several inner loop iterations
                    if (++innerLoopCount % 10 == 0)
                    {
                        await EditorTask.Yield();
                        // Report more granular progress considering both loops
                        var outerProgress = (float)currentDurationStep / durationStepCount;
                        var innerProgress = (float)currentInnerStepCount / (maxStartIndex - currentStartIndex + 1);
                        var combinedProgress = 0.1f + 0.8f * (outerProgress - 1f / durationStepCount + innerProgress / durationStepCount);
                        progressCallback?.Invoke(combinedProgress);
                    }

                    var sampleTime = cacheEntry.timePoints[startIndex];
                    if (sampleTime > maxStartTime)
                        continue;

                    // Find the index closest to our end time
                    var endSampleTime = sampleTime + targetDuration;
                    var endIndex = FindClosestTimePointIndex(cacheEntry.timePoints, endSampleTime);

                    // Ensure the calculated end point of the loop is within the search window.
                    if (endIndex > endTimeIndex)
                        break;

                    endSampleTime = cacheEntry.timePoints[endIndex]; // Use actual time point for precision

                    var (score, motionCoverage) = EvaluateLoopSimilarity(relevantBindings, cacheEntry.curveCache, cacheEntry.sampleCache,
                        sampleTime, endSampleTime, velocityDelta, amplitudeCheckBindings, totalOriginalAmplitude);

                    if (score < attemptBestScore && motionCoverage >= minimumMotionCoverage)
                    {
                        attemptBestScore = score;
                        attemptBestStart = sampleTime;
                        attemptBestMotionCoverageRatio = motionCoverage;
                    }
                }

                if (attemptBestScore < globalBestScore)
                {
                    globalBestScore = attemptBestScore;
                    globalBestStart = attemptBestStart;
                    globalBestEnd = attemptBestStart + targetDuration;
                    globalBestMotionCoverageRatio = attemptBestMotionCoverageRatio;
                    if (globalBestScore <= k_ExcellentLoopScore)
                        break;
                }
            }

            // Final progress
            progressCallback?.Invoke(1.0f);

            // Calculate the normalized score (0-1 where 1 is perfect)
            normalizedScore = 1f - Mathf.Clamp01(globalBestScore);

            if (globalBestScore <= muscleMatchingTolerance)
            {
                startTime = globalBestStart;
                endTime = globalBestEnd;

                if (Unsupported.IsDeveloperMode())
                    Debug.Log($"Match found. Scores (Match, Motion): ({100 * normalizedScore:F1}%, " +
                        $"{100 * Mathf.Clamp01(globalBestMotionCoverageRatio):F1}%)\nInterval: {startTime:F2}s - {endTime:F2}s");

                return (true, startTime, endTime, startTime / clip.length, endTime / clip.length, normalizedScore);
            }

            if (Unsupported.IsDeveloperMode())
                Debug.Log($"Match failed. Scores (Match, Motion): ({100 * normalizedScore:F1}%, " +
                    $"{100 * Mathf.Clamp01(globalBestMotionCoverageRatio):F1}%)\nInterval: {startTime:F2}s - {endTime:F2}s");

            return (false, startTime, endTime, startTime / clip.length, endTime / clip.length, normalizedScore);
        }

        /// <summary>
        /// Finds the index of the closest time point to the target time
        /// </summary>
        static int FindClosestTimePointIndex(float[] timePoints, float targetTime)
        {
            if (timePoints == null || timePoints.Length == 0)
                return -1;

            if (targetTime <= timePoints[0])
                return 0;

            if (targetTime >= timePoints[^1])
                return timePoints.Length - 1;

            var min = 0;
            var max = timePoints.Length - 1;

            while (min <= max)
            {
                var mid = (min + max) / 2;

                if (Mathf.Approximately(timePoints[mid], targetTime))
                    return mid;

                if (timePoints[mid] < targetTime)
                    min = mid + 1;
                else
                    max = mid - 1;
            }

            // After binary search, min > max
            return (min < timePoints.Length &&
                    (min == 0 || targetTime - timePoints[max] > timePoints[min] - targetTime))
                    ? min : max;
        }

        static ClipCacheEntry GetOrCreateClipCache(AnimationClip clip)
        {
            if (k_ClipCache.TryGetValue(clip, out var cacheEntry))
            {
                if (!Mathf.Approximately(cacheEntry.clipLength, clip.length))
                {
                    k_ClipCache.Remove(clip);
                    k_LruList.Remove(clip);
                }
                else
                {
                    // Move to front of LRU list (most recently used)
                    k_LruList.Remove(clip);
                    k_LruList.AddFirst(clip);
                    return cacheEntry;
                }
            }

            if (k_ClipCache.Count >= k_MaxCachedClips)
                CleanupCache();

            cacheEntry = new ClipCacheEntry
            {
                clipLength = clip.length
            };

            // Create uniform time points based on sample rate
            var numTimePoints = Mathf.CeilToInt(clip.length * k_SamplesPerSecond) + 1;
            cacheEntry.timePoints = new float[numTimePoints];

            var timeStep = clip.length / (numTimePoints - 1);
            for (var i = 0; i < numTimePoints; i++)
                cacheEntry.timePoints[i] = i * timeStep;

            k_ClipCache[clip] = cacheEntry;
            k_LruList.AddFirst(clip); // Add to front of LRU list (most recently used)
            return cacheEntry;
        }

        /// <summary>
        /// Removes the least recently used entry from the cache when it grows too large
        /// </summary>
        static void CleanupCache()
        {
            if (k_LruList.Count > 0)
            {
                var clipToRemove = k_LruList.Last.Value;
                k_LruList.RemoveLast();
                k_ClipCache.Remove(clipToRemove);
            }
        }

        /// <summary>
        /// Clears the static curve evaluation cache
        /// </summary>
        public static void ClearCache()
        {
            k_ClipCache.Clear();
            k_LruList.Clear();
        }

        static float CalculateMotionAmplitude(List<EditorCurveBinding> bindings, Dictionary<EditorCurveBinding, AnimationCurve> curveCache, float startTime,
            float endTime)
        {
            if (bindings.Count == 0)
                return 0f;

            var totalAmplitude = 0f;
            var timeStep = (endTime - startTime) / k_AmplitudeSampleCount;

            foreach (var binding in bindings)
            {
                var curve = curveCache[binding];
                var minVal = float.MaxValue;
                var maxVal = float.MinValue;

                for (var i = 0; i <= k_AmplitudeSampleCount; i++)
                {
                    var sampleTime = startTime + i * timeStep;
                    var val = curve.Evaluate(sampleTime);
                    if (val < minVal)
                        minVal = val;
                    if (val > maxVal)
                        maxVal = val;
                }
                totalAmplitude += maxVal - minVal;
            }

            return totalAmplitude;
        }

        static (float averageDifference, float motionCoverageRatio) EvaluateLoopSimilarity(List<EditorCurveBinding> relevantBindings, Dictionary<EditorCurveBinding, AnimationCurve> curveCache,
            Dictionary<(EditorCurveBinding, float), float> sampleCache, float timeA, float timeB, float velocityDelta,
            List<EditorCurveBinding> amplitudeBindings, float totalOriginalAmplitude)
        {
            var totalPoseDifference = 0f;
            var totalVelocityDifference = 0f;
            var validCurves = 0;

            foreach (var binding in relevantBindings)
            {
                var curve = curveCache[binding];
                if (!sampleCache.TryGetValue((binding, timeA), out var valueA))
                {
                    valueA = curve.Evaluate(timeA);
                    sampleCache[(binding, timeA)] = valueA;
                }

                if (!sampleCache.TryGetValue((binding, timeB), out var valueB))
                {
                    valueB = curve.Evaluate(timeB);
                    sampleCache[(binding, timeB)] = valueB;
                }
                totalPoseDifference += Mathf.Abs(valueB - valueA) * GetPositionWeight(binding);

                if (IsVelocityTracked(binding))
                {
                    float velA, velB;
                    if (sampleCache.TryGetValue((binding, timeA + velocityDelta), out var valueAPlus) &&
                        sampleCache.TryGetValue((binding, timeA - velocityDelta), out var valueAMinus))
                    {
                        velA = (valueAPlus - valueAMinus) / (2 * velocityDelta);
                    }
                    else
                    {
                        var aPlus = curve.Evaluate(timeA + velocityDelta);
                        var aMinus = curve.Evaluate(timeA - velocityDelta);
                        sampleCache[(binding, timeA + velocityDelta)] = aPlus;
                        sampleCache[(binding, timeA - velocityDelta)] = aMinus;
                        velA = (aPlus - aMinus) / (2 * velocityDelta);
                    }
                    if (sampleCache.TryGetValue((binding, timeB + velocityDelta), out var valueBPlus) &&
                        sampleCache.TryGetValue((binding, timeB - velocityDelta), out var valueBMinus))
                    {
                        velB = (valueBPlus - valueBMinus) / (2 * velocityDelta);
                    }
                    else
                    {
                        var bPlus = curve.Evaluate(timeB + velocityDelta);
                        var bMinus = curve.Evaluate(timeB - velocityDelta);
                        sampleCache[(binding, timeB + velocityDelta)] = bPlus;
                        sampleCache[(binding, timeB - velocityDelta)] = bMinus;
                        velB = (bPlus - bMinus) / (2 * velocityDelta);
                    }
                    totalVelocityDifference += Mathf.Abs(velB - velA) * k_VelocityMatchWeight;
                }
                validCurves++;
            }

            var motionCoverageRatio = 0f;
            if (totalOriginalAmplitude > 0)
            {
                var candidateAmplitude = CalculateMotionAmplitude(amplitudeBindings, curveCache, timeA, timeB);
                motionCoverageRatio = candidateAmplitude / totalOriginalAmplitude;
            }

            var averageDifference = validCurves > 0 ? (totalPoseDifference + totalVelocityDifference) / validCurves : 0;
            return (averageDifference, motionCoverageRatio);
        }

        static bool IsAmplitudeTracked(EditorCurveBinding binding)
        {
            var lowerMuscleName = binding.propertyName.ToLowerInvariant();
            return lowerMuscleName.Contains("arm") || lowerMuscleName.Contains("leg");
        }

        static bool IsVelocityTracked(EditorCurveBinding binding)
        {
            var muscleName = binding.propertyName;

            var lowerMuscleName = muscleName.ToLowerInvariant();
            return lowerMuscleName.Contains("arm") || lowerMuscleName.Contains("leg") || lowerMuscleName.Contains("hip");
        }

        static float GetPositionWeight(EditorCurveBinding binding)
        {
            var muscleName = binding.propertyName;

            // Special case for root vertical motion
            if (muscleName == "RootT.y")
                return k_RootVerticalWeight;

            var lowerMuscleName = muscleName.ToLowerInvariant();
            if (lowerMuscleName.Contains("arm") || lowerMuscleName.Contains("leg") || lowerMuscleName.Contains("hip"))
                return k_MajorBoneRotationWeight;

            return 1.0f;
        }

        public static void FlattenRootMotion(this AnimationClip clip)
        {
            var curveBindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in curveBindings)
            {
                if (binding.type != typeof(Animator) || (binding.propertyName != "RootT.x" && binding.propertyName != "RootT.z"))
                    continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.keys.Length == 0)
                    continue;

                var initialValue = curve.keys[0].value;
                var newKeys = new Keyframe[2];
                newKeys[0] = new Keyframe(0f, initialValue) { inTangent = 0f, outTangent = 0f };
                newKeys[1] = new Keyframe(clip.length, initialValue) { inTangent = 0f, outTangent = 0f };
                var newCurve = new AnimationCurve(newKeys);
                AnimationUtility.SetEditorCurve(clip, binding, newCurve);
            }
            EditorUtility.SetDirty(clip);
        }

        /// <summary>
        /// Re-orients an entire animation clip so that its initial rotation has zero yaw (Y-axis rotation).
        /// This is useful for normalizing animations that start facing the wrong direction.
        /// It preserves all relative turning within the clip.
        /// </summary>
        public static void NormalizeRootRotation(this AnimationClip clip)
        {
            if (clip == null)
                return;

            // Find the bindings for the root rotation quaternion
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var qxBinding = bindings.FirstOrDefault(b => b.propertyName == "RootQ.x");
            var qyBinding = bindings.FirstOrDefault(b => b.propertyName == "RootQ.y");
            var qzBinding = bindings.FirstOrDefault(b => b.propertyName == "RootQ.z");
            var qwBinding = bindings.FirstOrDefault(b => b.propertyName == "RootQ.w");

            // If there's no root rotation, there's nothing to do
            if (qxBinding.path == null || qyBinding.path == null || qzBinding.path == null || qwBinding.path == null)
            {
                Debug.LogWarning($"Clip '{clip.name}' has no RootQ curves to normalize.", clip);
                return;
            }

            var qxCurve = AnimationUtility.GetEditorCurve(clip, qxBinding);
            var qyCurve = AnimationUtility.GetEditorCurve(clip, qyBinding);
            var qzCurve = AnimationUtility.GetEditorCurve(clip, qzBinding);
            var qwCurve = AnimationUtility.GetEditorCurve(clip, qwBinding);

            if (qxCurve == null || qyCurve == null || qzCurve == null || qwCurve == null)
            {
                Debug.LogWarning($"Could not retrieve all RootQ curves for clip '{clip.name}'.", clip);
                return;
            }

            // 1. Get the rotation at the very start of the clip
            var initialRotation = new Quaternion(
                qxCurve.Evaluate(0f),
                qyCurve.Evaluate(0f),
                qzCurve.Evaluate(0f),
                qwCurve.Evaluate(0f)
            ).normalized;

            // 2. Isolate the yaw (Y-axis rotation) and create a correction quaternion
            var initialYaw = initialRotation.eulerAngles.y;
            var correction = Quaternion.Euler(0, -initialYaw, 0);

            // 3. Gather all unique keyframe times from all four curves
            var allKeyTimes = new HashSet<float>();
            foreach (var key in qxCurve.keys) allKeyTimes.Add(key.time);
            foreach (var key in qyCurve.keys) allKeyTimes.Add(key.time);
            foreach (var key in qzCurve.keys) allKeyTimes.Add(key.time);
            foreach (var key in qwCurve.keys) allKeyTimes.Add(key.time);

            var sortedTimes = allKeyTimes.ToList();
            sortedTimes.Sort();

            var newQxKeys = new List<Keyframe>(sortedTimes.Count);
            var newQyKeys = new List<Keyframe>(sortedTimes.Count);
            var newQzKeys = new List<Keyframe>(sortedTimes.Count);
            var newQwKeys = new List<Keyframe>(sortedTimes.Count);

            // 4. For each unique key time, apply the correction
            foreach (var time in sortedTimes)
            {
                var originalRot = new Quaternion(
                    qxCurve.Evaluate(time),
                    qyCurve.Evaluate(time),
                    qzCurve.Evaluate(time),
                    qwCurve.Evaluate(time)
                );

                // Apply the correction to the original rotation
                var correctedRot = correction * originalRot;

                newQxKeys.Add(new Keyframe(time, correctedRot.x));
                newQyKeys.Add(new Keyframe(time, correctedRot.y));
                newQzKeys.Add(new Keyframe(time, correctedRot.z));
                newQwKeys.Add(new Keyframe(time, correctedRot.w));
            }

            // 5. Create new curves and replace the old ones
            AnimationUtility.SetEditorCurve(clip, qxBinding, new AnimationCurve(newQxKeys.ToArray()));
            AnimationUtility.SetEditorCurve(clip, qyBinding, new AnimationCurve(newQyKeys.ToArray()));
            AnimationUtility.SetEditorCurve(clip, qzBinding, new AnimationCurve(newQzKeys.ToArray()));
            AnimationUtility.SetEditorCurve(clip, qwBinding, new AnimationCurve(newQwKeys.ToArray()));

            EditorUtility.SetDirty(clip);
        }

        /// <summary>
        /// Normalizes the root transform of an animation clip.
        /// This is a combined operation that first normalizes the root rotation so the animation starts
        /// facing forward (along the world Z-axis) and then normalizes the root position so the animation
        /// starts at the origin (0, 0, 0).
        /// This method is more robust than calling NormalizeRootRotation and NormalizeRootMotion separately,
        /// as it correctly rotates the entire motion path before translating it to the origin.
        /// It preserves relative motion and rotation throughout the clip. This is generally the
        /// recommended normalization function for animations with both translation and rotation.
        /// </summary>
        /// <param name="clip">The animation clip to normalize.</param>
        public static void NormalizeRootTransform(this AnimationClip clip)
        {
            if (clip == null)
                return;

            var bindings = AnimationUtility.GetCurveBindings(clip);

            // Find position and rotation bindings
            var txBinding = bindings.FirstOrDefault(b => b.propertyName == "RootT.x");
            var tzBinding = bindings.FirstOrDefault(b => b.propertyName == "RootT.z");
            var qxBinding = bindings.FirstOrDefault(b => b.propertyName == "RootQ.x");
            var qyBinding = bindings.FirstOrDefault(b => b.propertyName == "RootQ.y");
            var qzBinding = bindings.FirstOrDefault(b => b.propertyName == "RootQ.z");
            var qwBinding = bindings.FirstOrDefault(b => b.propertyName == "RootQ.w");

            // Get curves
            var txCurve = AnimationUtility.GetEditorCurve(clip, txBinding);
            var tzCurve = AnimationUtility.GetEditorCurve(clip, tzBinding);
            var qxCurve = AnimationUtility.GetEditorCurve(clip, qxBinding);
            var qyCurve = AnimationUtility.GetEditorCurve(clip, qyBinding);
            var qzCurve = AnimationUtility.GetEditorCurve(clip, qzBinding);
            var qwCurve = AnimationUtility.GetEditorCurve(clip, qwBinding);

            var hasPosition = txCurve != null && tzCurve != null;
            var hasRotation = qxCurve != null && qyCurve != null && qzCurve != null && qwCurve != null;

            if (!hasPosition && !hasRotation)
            {
                Debug.LogWarning($"Clip '{clip.name}' has no root transform curves to normalize.", clip);
                return;
            }

            // --- 1. Calculate Initial State and Correction ---
            var initialPosition = hasPosition
                ? new Vector3(txCurve.Evaluate(0f), 0, tzCurve.Evaluate(0f))
                : Vector3.zero;

            var correction = Quaternion.identity;
            if (hasRotation)
            {
                var initialRotation = new Quaternion(
                    qxCurve.Evaluate(0f),
                    qyCurve.Evaluate(0f),
                    qzCurve.Evaluate(0f),
                    qwCurve.Evaluate(0f)
                ).normalized;

                var initialYaw = initialRotation.eulerAngles.y;
                correction = Quaternion.Euler(0, -initialYaw, 0);
            }

            // --- 2. Gather all unique keyframe times ---
            var allKeyTimes = new HashSet<float>();
            if (hasPosition)
            {
                if (txCurve.keys.Length > 0) foreach (var key in txCurve.keys) allKeyTimes.Add(key.time);
                if (tzCurve.keys.Length > 0) foreach (var key in tzCurve.keys) allKeyTimes.Add(key.time);
            }
            if (hasRotation)
            {
                if (qxCurve.keys.Length > 0) foreach (var key in qxCurve.keys) allKeyTimes.Add(key.time);
                if (qyCurve.keys.Length > 0) foreach (var key in qyCurve.keys) allKeyTimes.Add(key.time);
                if (qzCurve.keys.Length > 0) foreach (var key in qzCurve.keys) allKeyTimes.Add(key.time);
                if (qwCurve.keys.Length > 0) foreach (var key in qwCurve.keys) allKeyTimes.Add(key.time);
            }

            // If there are no keys, process the start and end of the clip to normalize the constant pose.
            if (allKeyTimes.Count == 0 && (hasPosition || hasRotation))
            {
                allKeyTimes.Add(0f);
                if (clip.length > 0.0001f)
                {
                    allKeyTimes.Add(clip.length);
                }
            }

            if (allKeyTimes.Count == 0)
                return; // Nothing to process

            var sortedTimes = allKeyTimes.ToList();
            sortedTimes.Sort();

            // --- 3. Create new keyframes by transforming each point ---
            var newTxKeys = hasPosition ? new List<Keyframe>(sortedTimes.Count) : null;
            var newTzKeys = hasPosition ? new List<Keyframe>(sortedTimes.Count) : null;
            var newQxKeys = hasRotation ? new List<Keyframe>(sortedTimes.Count) : null;
            var newQyKeys = hasRotation ? new List<Keyframe>(sortedTimes.Count) : null;
            var newQzKeys = hasRotation ? new List<Keyframe>(sortedTimes.Count) : null;
            var newQwKeys = hasRotation ? new List<Keyframe>(sortedTimes.Count) : null;

            foreach (var time in sortedTimes)
            {
                if (hasPosition)
                {
                    var originalPos = new Vector3(txCurve.Evaluate(time), 0, tzCurve.Evaluate(time));
                    var relativePos = originalPos - initialPosition;
                    var correctedPos = correction * relativePos;

                    newTxKeys.Add(new Keyframe(time, correctedPos.x));
                    newTzKeys.Add(new Keyframe(time, correctedPos.z));
                }

                if (hasRotation)
                {
                    var originalRot = new Quaternion(
                        qxCurve.Evaluate(time),
                        qyCurve.Evaluate(time),
                        qzCurve.Evaluate(time),
                        qwCurve.Evaluate(time)
                    );

                    var correctedRot = correction * originalRot;

                    newQxKeys.Add(new Keyframe(time, correctedRot.x));
                    newQyKeys.Add(new Keyframe(time, correctedRot.y));
                    newQzKeys.Add(new Keyframe(time, correctedRot.z));
                    newQwKeys.Add(new Keyframe(time, correctedRot.w));
                }
            }

            // --- 4. Apply new curves to the clip ---
            if (hasPosition)
            {
                AnimationUtility.SetEditorCurve(clip, txBinding, new AnimationCurve(newTxKeys.ToArray()));
                AnimationUtility.SetEditorCurve(clip, tzBinding, new AnimationCurve(newTzKeys.ToArray()));
            }
            if (hasRotation)
            {
                AnimationUtility.SetEditorCurve(clip, qxBinding, new AnimationCurve(newQxKeys.ToArray()));
                AnimationUtility.SetEditorCurve(clip, qyBinding, new AnimationCurve(newQyKeys.ToArray()));
                AnimationUtility.SetEditorCurve(clip, qzBinding, new AnimationCurve(newQzKeys.ToArray()));
                AnimationUtility.SetEditorCurve(clip, qwBinding, new AnimationCurve(newQwKeys.ToArray()));
            }

            EditorUtility.SetDirty(clip);
        }
    }
}
