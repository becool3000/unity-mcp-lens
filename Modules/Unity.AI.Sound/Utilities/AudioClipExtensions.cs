using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Sound.Services.Utilities
{
    static class AudioClipExtensions
    {
        public static async Task Play(this AudioClip audioClip, CancellationToken token, Action<float> timeUpdate = null, SoundEnvelopeSettings envelopeSettings = null)
        {
            EditorUtility.audioMasterMute = false;

            var playClip = audioClip;
            var timeOffset = 0.0f;
            if (envelopeSettings != null)
            {
                audioClip.TryGetSamples(out var samples);
                audioClip.ApplyEnvelope(samples, envelopeSettings.controlPoints);
                // trimming before enveloping would be more efficient, but also, more error-prone
                samples = audioClip.ApplyTrim(samples, envelopeSettings.startPosition, envelopeSettings.endPosition);
                playClip = AudioClip.Create(audioClip.name + "_clone", samples.Length / Math.Max(1, audioClip.channels), audioClip.channels, audioClip.frequency, false);
                playClip.SetData(samples, 0);

                timeOffset = envelopeSettings.startPosition * audioClip.length;
            }

            var audioSource = new GameObject("AudioClipPlayer") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<AudioSource>();
            audioSource.clip = playClip;
            audioSource.Play();

            while (audioSource.isPlaying)
            {
                try { timeUpdate?.Invoke(timeOffset + audioSource.time); }
                catch { /* ignored */ }

                if (token.IsCancellationRequested)
                    break;
                await EditorTask.Yield();
            }

            if (audioSource.isPlaying)
                audioSource.Stop();

            try { timeUpdate?.Invoke(0); }
            catch { /* ignored */ }

            audioSource.gameObject.SafeDestroy();
            if (envelopeSettings != null)
                playClip.SafeDestroy();
        }
        public static Task Play(this AudioClip audioClip, Action<float> timeUpdate = null, SoundEnvelopeSettings envelopeSettings = null) =>
            Play(audioClip, CancellationToken.None, timeUpdate, envelopeSettings);

        public static SoundEnvelopeSettings MakeDefaultEnvelope(this AudioClip audioClip, float startPosition = 0, float endPosition = 1)
        {
            // auto fade in-out
            const float fadeInOut = 0.005f; // seconds
            return new SoundEnvelopeSettings {
                startPosition = startPosition,
                endPosition = endPosition,
                controlPoints = new List<Vector2>
                {
                    new(Mathf.Max(0, startPosition), 0),
                    new(Mathf.Max(0, startPosition + GetNormalizedPositionAtTime(audioClip, fadeInOut)), 1),
                    new(Mathf.Min(1, endPosition - GetNormalizedPositionAtTime(audioClip, fadeInOut)), 1),
                    new(Mathf.Min(1, endPosition), 0)
                }};
        }

        static int GetSampleIndexAtPosition(this AudioClip audioClip, float position) => Mathf.FloorToInt(Mathf.Clamp01(position) * audioClip.samples);

        static int GetSampleIndexAtPositionUnclamped(this AudioClip audioClip, float position) => Mathf.FloorToInt(position * audioClip.samples);

        public static float GetNormalizedPositionAtSampleIndex(this AudioClip audioClip, int sample) => Mathf.Clamp01(sample / (float)audioClip.samples);

        public static float GetNormalizedPositionAtTime(this AudioClip audioClip, float time) => Mathf.Clamp01(time / audioClip.length);

        public static float GetNormalizedPositionAtTimeUnclamped(this AudioClip audioClip, float time) => audioClip.length < Mathf.Epsilon ? -1f : time / audioClip.length;

        public static void CreateSilentAudioWavFile(Stream outputStream) => EncodeToWav(new[] { silentSample }, outputStream);

        static void WriteWavHeader(BinaryWriter writer, int numSamples, int numChannels, int sampleRate, int bitsPerSample)
        {
            var bytesPerSample = bitsPerSample / 8;
            var blockAlign = numChannels * bytesPerSample;
            var byteRate = sampleRate * blockAlign;
            var dataChunkSize = numSamples * bytesPerSample;
            const int subChunk1Size = 16; // PCM header size for 'fmt ' chunk
            var subChunk2Size = dataChunkSize;
            var chunkSize = 4 + (8 + subChunk1Size) + (8 + subChunk2Size);

            // RIFF header
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(chunkSize);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });

            // 'fmt ' sub-chunk
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(subChunk1Size);
            writer.Write((short)1);                   // Audio format (1 = PCM)
            writer.Write((short)numChannels);         // Number of channels
            writer.Write(sampleRate);                 // Sample rate
            writer.Write(byteRate);                   // Byte rate
            writer.Write((short)blockAlign);          // Block align
            writer.Write((short)bitsPerSample);       // Bits per sample

            // 'data' sub-chunk
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(subChunk2Size);
        }

        static void EncodeToWav(IReadOnlyCollection<float> samples, Stream outputStream, int numChannels = 1, int sampleRate = 44100)
        {
            // Audio Specifications
            const int bitsPerSample = 16; // Bits per sample
            var numSamples = samples.Count * numChannels;

            using var writer = new BinaryWriter(outputStream, System.Text.Encoding.ASCII, true);
            WriteWavHeader(writer, numSamples, numChannels, sampleRate, bitsPerSample);

            // Write audio data
            foreach (var sample in samples)
                writer.Write((short)(sample * short.MaxValue));

            writer.Flush();
        }

        public static async Task EncodeToWavAsync(this AudioClip audioClip, Stream outputStream, SoundEnvelopeSettings envelopeSettings = null)
        {
            if (!TryGetSamples(audioClip, out var audioSamples))
                return;
            envelopeSettings ??= new SoundEnvelopeSettings();
            ApplyEnvelope(audioClip, audioSamples, envelopeSettings.controlPoints);
            var samples = GetSampleRange(audioClip, audioSamples, envelopeSettings.startPosition, envelopeSettings.endPosition);
            await EncodeToWavAsync(samples, outputStream, audioClip.channels, audioClip.frequency);
        }

        public static async Task EncodeToWavAsync(this AudioClip audioClip, Stream outputStream, float[] audioSamples = null, SoundEnvelopeSettings envelopeSettings = null)
        {
            if (audioSamples == null && !audioClip.TryGetSamples(out audioSamples))
                return;

            envelopeSettings ??= new SoundEnvelopeSettings();
            ApplyEnvelope(audioClip, audioSamples, envelopeSettings.controlPoints);

            var samples = GetSampleRange(audioClip, audioSamples, envelopeSettings.startPosition, envelopeSettings.endPosition);
            await EncodeToWavAsync(samples, outputStream, audioClip.channels, audioClip.frequency);
        }

        public static async Task EncodeToWavAsync(IReadOnlyCollection<float> samples, Stream outputStream, int numChannels = 1, int sampleRate = 44100)
        {
            // Audio Specifications
            const int bitsPerSample = 16; // Bits per sample
            var numSamples = samples.Count * numChannels;

            // Create a memory stream to write header data
            using var memStream = new MemoryStream();
            using (var writer = new BinaryWriter(memStream, System.Text.Encoding.ASCII, true))
            {
                WriteWavHeader(writer, numSamples, numChannels, sampleRate, bitsPerSample);
            }

            // Write header to output stream
            memStream.Position = 0;
            await memStream.CopyToAsync(outputStream);

            // Write audio data
            var buffer = new byte[4096];
            var bufferPosition = 0;

            foreach (var sample in samples)
            {
                var sampleValue = (short)(sample * short.MaxValue);

                // Write bytes of the short value to buffer
                buffer[bufferPosition++] = (byte)(sampleValue & 0xFF);
                buffer[bufferPosition++] = (byte)((sampleValue >> 8) & 0xFF);

                // If buffer is full, write it to stream
                if (bufferPosition >= buffer.Length - 1) // Leave room for at least one more sample
                {
                    await outputStream.WriteAsync(buffer, 0, bufferPosition);
                    bufferPosition = 0;
                }
            }

            // Write any remaining data in the buffer
            if (bufferPosition > 0)
                await outputStream.WriteAsync(buffer, 0, bufferPosition);

            await outputStream.FlushAsync();
        }

        public static async Task EncodeToWavUnclampedAsync(this AudioClip audioClip, Stream outputStream, float start = 0, float end = -1)
        {
            if (!audioClip.TryGetSamples(out var audioSamples))
                return;

            var samples = GetSampleRange(audioClip, audioSamples, start, end);

            var startSample = start == 0 ? 0 : audioClip.GetSampleIndexAtPosition(start);
            var endSample = (int)end == -1 ? audioClip.samples : audioClip.GetSampleIndexAtPosition(end);
            var sampleLength = endSample - startSample;

            var startSampleUnclamped = start == 0 ? 0 : audioClip.GetSampleIndexAtPositionUnclamped(start);
            var endSampleUnclamped = (int)end == -1 ? audioClip.samples : audioClip.GetSampleIndexAtPositionUnclamped(end);
            var sampleLengthUnclamped = endSampleUnclamped - startSampleUnclamped;
            if (sampleLengthUnclamped > sampleLength)
            {
                var previousLength = samples.Length;
                Array.Resize(ref samples, sampleLengthUnclamped * audioClip.channels);
                for (var i = previousLength - 1; i < samples.Length; i++)
                    samples[i] = silentSample;

                if (startSampleUnclamped < startSample)
                    OffsetArrayInPlace(samples, (startSample - startSampleUnclamped) * audioClip.channels);
            }

            await EncodeToWavAsync(samples, outputStream, audioClip.channels, audioClip.frequency);
        }

        public static async Task SaveAudioClipToWavAsync(this AudioClip audioClip, string assetPath, SoundEnvelopeSettings settings)
        {
            settings ??= new SoundEnvelopeSettings();
            {
                await using var fileStream = FileIO.OpenWriteAsync(assetPath);
                await EncodeToWavAsync(audioClip, fileStream, settings);
            }
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath));
        }

        internal const float silentSample = 0.0f;

        static void OffsetArrayInPlace(IList<float> array, int offsetCount)
        {
            for (var i = array.Count - 1; i >= offsetCount; i--)
                array[i] = array[i - offsetCount];

            for (var i = 0; i < offsetCount; i++)
                array[i] = silentSample;
        }

        public static float[] GetSampleRange(this AudioClip audioClip, float[] audioSamples = null, float start = 0, float end = -1)
        {
            if (audioSamples == null && !audioClip.TryGetSamples(out audioSamples))
                return null;
            var startSample = start == 0 ? 0 : GetSampleIndexAtPosition(audioClip, start);
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            var endSample = end == -1 ? audioClip.samples : GetSampleIndexAtPosition(audioClip, end);
            var sampleLength = endSample - startSample;

            var samples = new float[sampleLength * audioClip.channels];
            Array.Copy(audioSamples, startSample * audioClip.channels, samples, 0, samples.Length);

            return samples;
        }

        public static bool TryGetSamples(this AudioClip audioClip, out float[] samples)
        {
            samples = null;
            if (audioClip == null)
                return false;
            samples = new float[audioClip.samples * audioClip.channels];
            audioClip.GetData(samples, 0);
            return true;
        }

        static float InterpolateAmplitude(float time, Vector2 startKeyframe, Vector2 endKeyframe)
        {
            float amplitude;
            if (Mathf.Approximately(startKeyframe.x, endKeyframe.x))
                amplitude = startKeyframe.y;
            else if (time <= startKeyframe.x)
                amplitude = startKeyframe.y;
            else if (time >= endKeyframe.x)
                amplitude = endKeyframe.y;
            else
                amplitude = Mathf.Lerp(startKeyframe.y, endKeyframe.y, (time - startKeyframe.x) / (endKeyframe.x - startKeyframe.x));
            return amplitude;
        }

        static void ApplyEnvelope(this AudioClip audioClip, IList<float> audioData, IReadOnlyList<Vector2> controlPoints)
        {
            if (controlPoints == null || controlPoints.Count == 0)
                return;

            var pointA = controlPoints[0];
            var pointB = controlPoints[Math.Min(1, controlPoints.Count - 1)];

            var controlPointIndex = 0;
            for (var i = 0; i < audioClip.samples; i++)
            {
                var samplePosition = i / (float)audioClip.samples;
                while (samplePosition > pointB.x && controlPointIndex < controlPoints.Count - 1)
                {
                    controlPointIndex++;
                    pointA = controlPoints[controlPointIndex];
                    pointB = controlPoints[Math.Min(controlPointIndex + 1, controlPoints.Count - 1)];
                }

                var amplitudeScale = InterpolateAmplitude(samplePosition, pointA, pointB);
                for (var channel = 0; channel < audioClip.channels; channel++)
                {
                    var index = i * audioClip.channels + channel;
                    audioData[index] *= amplitudeScale;
                }
            }
        }

        static float[] ApplyTrim(this AudioClip audioClip, float[] samples, float start = 0, float end = -1)
        {
            var startSample = (start == 0) ? 0 : GetSampleIndexAtPosition(audioClip, start) * audioClip.channels;
            var endSample = (end == -1) ? audioClip.samples * audioClip.channels : GetSampleIndexAtPosition(audioClip, end) * audioClip.channels;
            var count =  Math.Max(0, endSample - startSample);
            var subSamples = new float[count];
            Array.Copy(samples, startSample, subSamples, 0, count);
            return subSamples;
        }

        public static int FindPreviousSilentSample(this AudioClip audioClip, IList<float> audioData, float maxAmplitude,
            int startSample, float amplitudeThreshold = 0.02f, int durationThreshold = 100 /*0.05s*/)
        {
            var silentCount = durationThreshold;
            var silenceStartSample = -1;
            for (var i = startSample; i >= 0; i--)
            {
                for (var channel = 0; channel < audioClip.channels; channel++)
                {
                    var index = i * audioClip.channels + channel;
                    if (Mathf.Abs(audioData[index]) <= Mathf.Max(0, amplitudeThreshold * maxAmplitude - Mathf.Epsilon))
                    {
                        silentCount--;
                        if (silenceStartSample < 0)
                            silenceStartSample = i;
                        if (silentCount <= 0)
                            return silenceStartSample;
                    }
                    else
                    {
                        silentCount = durationThreshold;
                        silenceStartSample = -1;
                    }
                }
            }

            return 0;
        }

        public static int FindNextSilentSample(this AudioClip audioClip, IList<float> audioData, float maxAmplitude, int startSample,
            float amplitudeThreshold = 0.02f, int durationThreshold = 100 /*0.05s*/)
        {
            var silentCount = durationThreshold;
            var silenceStartSample = -1;
            for (var i = startSample; i < audioClip.samples; i++)
            {
                for (var channel = 0; channel < audioClip.channels; channel++)
                {
                    var index = i * audioClip.channels + channel;
                    if (Mathf.Abs(audioData[index]) <= Mathf.Max(0, amplitudeThreshold * maxAmplitude - Mathf.Epsilon))
                    {
                        silentCount--;
                        if (silenceStartSample < 0)
                            silenceStartSample = i;
                        if (silentCount <= 0)
                            return silenceStartSample;
                    }
                    else
                    {
                        silentCount = durationThreshold;
                        silenceStartSample = -1;
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Trim an audio clip for a given start time and end time.
        /// </summary>
        /// <param name="audioClip">The source AudioClip from which to extract the segment.</param>
        /// <param name="startTimeInSeconds">The time in seconds where the new sub-clip should begin.</param>
        /// <param name="endTimeInSeconds">The time in seconds where the new sub-clip should end.</param>
        /// <returns>A new float array containing the audio samples of the specified segment.</returns>
        public static float[] TrimAudioFromStartAndEndTime(this AudioClip audioClip, float startTimeInSeconds, float endTimeInSeconds)
        {
            audioClip.TryGetSamples(out var audioSamples);
            if(startTimeInSeconds < 0 || endTimeInSeconds < startTimeInSeconds)
                throw new ArgumentException("Invalid startTime or endTime values.");

            var startSample = audioClip.GetSampleIndexAtPosition(audioClip.GetNormalizedPositionAtTime(startTimeInSeconds)) * audioClip.channels;
            var endSample = audioClip.GetSampleIndexAtPosition(audioClip.GetNormalizedPositionAtTime(endTimeInSeconds)) * audioClip.channels;
            var count = Math.Max(0, endSample - startSample);
            var subSamples = new float[count];
            Array.Copy(audioSamples, startSample, subSamples, 0, count);
            return subSamples;
        }

        /// <summary>
        /// Remove silences at the beginning and end of an audio clip.
        /// </summary>
        /// <param name="audioClip">The Audio Clip to remove the silences.</param>
        /// <returns>The audio samples of the Audio Clip with the start and end position where there are no silences.</returns>
        public static (float[] samples, float startPosition, float endPosition) TrimSilences(this AudioClip audioClip)
        {
            audioClip.TryGetSamples(out var audioSamples);
            var soundStartSample = audioClip.FindNextNonSilentSample(audioSamples, 0);
            var soundEndSample = audioClip.FindPreviousNonSilentSample(audioSamples, audioClip.samples - 1);
            var startPosition = audioClip.GetNormalizedPositionAtSampleIndex(soundStartSample);
            var endPosition = audioClip.GetNormalizedPositionAtSampleIndex(soundEndSample);

            if (endPosition <= startPosition + Mathf.Epsilon)
                endPosition = 1f;

            return (audioSamples, startPosition, endPosition);
        }

        static int FindNextNonSilentSample(this AudioClip audioClip, IList<float> audioData, int startSample,
            float amplitudeThreshold = 0.02f)
        {
            for (var i = startSample; i < audioClip.samples; i++)
            {
                for (var channel = 0; channel < audioClip.channels; channel++)
                {
                    var index = i * audioClip.channels + channel;
                    if (Mathf.Abs(audioData[index]) > amplitudeThreshold)
                    {
                        return i;
                    }
                }
            }
            return 0;
        }

        static int FindPreviousNonSilentSample(this AudioClip audioClip, IList<float> audioData, int startSample,
            float amplitudeThreshold = 0.02f)
        {
            for (var i = startSample; i >= 0; i--)
            {
                for (var channel = 0; channel < audioClip.channels; channel++)
                {
                    var index = i * audioClip.channels + channel;
                    if (Mathf.Abs(audioData[index]) > amplitudeThreshold)
                    {
                        return i;
                    }
                }
            }
            return 0;
        }

        public static (float, int) FindMaxAmplitudeAndSample(this AudioClip audioClip, IList<float> audioData)
        {
            float maxAmplitude = 0;
            var maxAmplitudeSample = 0;
            for (var i = 0; i < audioClip.samples; i++)
            {
                for (var channel = 0; channel < audioClip.channels; channel++)
                {
                    var index = i * audioClip.channels + channel;
                    if (Mathf.Abs(audioData[index]) > maxAmplitude)
                    {
                        maxAmplitudeSample = i;
                        maxAmplitude = Mathf.Abs(audioData[index]);
                    }
                }
            }

            return (maxAmplitude, maxAmplitudeSample);
        }

        /// <summary>
        /// Normalize an audio clip : Increase the volume of an audio file by the largest possible amount without causing any clipping.
        /// </summary>
        /// <param name="audioClip">Audio Clip to normalize.</param>
        /// <returns>The normalized audio samples.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the normalization is not possible, if the max amplitude is 0 or greater than 1.</exception>
        public static float[] NormalizeAudio(this AudioClip audioClip)
        {
            audioClip.TryGetSamples(out var audioSamples);
            var (maxAmplitude, maxAmplitudeSample) = audioClip.FindMaxAmplitudeAndSample(audioSamples);
            if (maxAmplitude is 0f or > 1f)
                throw new InvalidOperationException("Normalization is not possible on this Audio Clip");
            return audioClip.ChangeVolume(1f / maxAmplitude);
        }

        /// <summary>
        /// Increase or decrease the volume of an audio clip by a given factor. If clipping occurs, normalize instead.
        /// </summary>
        /// <param name="audioClip">Audio Clip to work on.</param>
        /// <param name="factor">The factor to increase or decrease the volume.</param>
        /// <param name="normalize">To force the normalization and ignore the factor.</param>
        /// <returns>The audio samples multiplied by the factor.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the factor is too big and is causing clipping.</exception>
        public static float[] ChangeVolume(this AudioClip audioClip, float factor, bool normalize = false)
        {
            audioClip.TryGetSamples(out var audioSamples);
            try
            {
                if (normalize)
                {
                    audioSamples = audioClip.NormalizeAudio();
                }
                else
                {
                    var (maxAmplitude, maxAmplitudeSample) = audioClip.FindMaxAmplitudeAndSample(audioSamples);

                    var clippingFactor = maxAmplitude * factor;
                    // Adds a comparison with 1f to avoid floating point precision issues. Sometimes, the value would be a little higher than 1f when
                    // coming from NormalizeAudio and would go into an infinite loop
                    if (clippingFactor > 1f && !Mathf.Approximately(clippingFactor, 1f))
                        throw new InvalidOperationException("Cannot increase volume by the requested factor without causing clipping.");

                    for (var i = 0; i < audioClip.samples; i++)
                    {
                        for (var channel = 0; channel < audioClip.channels; channel++)
                        {
                            var index = i * audioClip.channels + channel;
                            audioSamples[index] *= factor;
                        }
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // If there is clipping, normalize instead so we have the largest possible amount without causing any clipping.
                audioSamples = audioClip.NormalizeAudio();
            }

            return audioSamples;
        }

        /// <summary>
        /// Creates a loop by blending the end of the audio with the beginning to eliminate clicks with crossfading.
        /// </summary>
        /// <param name="audioClip">The Audio Clip to work on.</param>
        /// <param name="crossfadeDurationMs">The duration of the crossfade in milliseconds.</param>
        /// <returns>The new audio samples in a loop.</returns>
        public static float[] CreateCrossFade(this AudioClip audioClip, int crossfadeDurationMs)
        {
            if(crossfadeDurationMs <= 0)
                throw new ArgumentException("Crossfade duration must be greater than zero.", nameof(crossfadeDurationMs));

            audioClip.TryGetSamples(out var audioSamples);

            var crossfadeSamples = (int)(crossfadeDurationMs * audioClip.frequency / 1000f);
            var totalSamples = audioSamples.Length / audioClip.channels;
            crossfadeSamples = Math.Min(crossfadeSamples, totalSamples / 2); // Ensure we don't exceed half the audio length

            var looped = new float[audioSamples.Length];
            Array.Copy(audioSamples, looped, audioSamples.Length);

            for (int i = 0; i < crossfadeSamples; i++)
            {
                var position = i / (float)(crossfadeSamples - 1);
                var fadeIn = (float)Math.Sqrt(position);
                var fadeOut = (float)Math.Sqrt(1.0f - position);

                for (int channel = 0; channel < audioClip.channels; channel++)
                {
                    var index = i * audioClip.channels + channel;
                    var endIndex = audioSamples.Length - crossfadeSamples * audioClip.channels + i * audioClip.channels + channel;
                    looped[index] = audioSamples[index] * fadeIn + audioSamples[endIndex] * fadeOut;
                }
            }

            return looped;
        }
    }
}
