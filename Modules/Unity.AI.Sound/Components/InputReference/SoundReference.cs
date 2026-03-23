using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Sound.Services.Stores.Actions;
using Unity.AI.Sound.Services.Stores.Selectors;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Sound.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using AssetReferenceExtensions = Unity.AI.Sound.Services.Utilities.AssetReferenceExtensions;
using Debug = UnityEngine.Debug;

namespace Unity.AI.Sound.Components
{
    [UxmlElement]
    partial class SoundReference : VisualElement, IInputReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Sound/Components/InputReference/SoundReference.uxml";

        readonly Button m_RecordButton;
        readonly Toggle m_OverwriteAssetToggle;
        readonly DropdownField m_MicrophonesDropdownField;
        readonly ObjectField m_AudioObjectField;

        AudioClip m_AudioClipRecorded;
        float m_TimeElapsedInMs;
        CancellationTokenSource m_RecordCancellationTokenSource;

        public SoundReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            this.Bind(
                GenerationSettingsActions.setSoundReferenceAsset,
                Selectors.SelectSoundReferenceAsset);
            this.BindWithStrength(
                GenerationSettingsActions.setSoundReferenceStrength,
                GenerationSettingsActions.setSoundReference,
                Selectors.SelectSoundReferenceStrength);

            m_AudioObjectField = this.Q<ObjectField>();

            m_RecordButton = this.Q<Button>("record");
            m_RecordButton.clickable = new Clickable(async _ =>
            {
                await ToggleRecording();
            });

            m_OverwriteAssetToggle = this.Q<Toggle>("overwrite-asset");
            m_OverwriteAssetToggle.RegisterValueChangedCallback(evt =>
            {
                this.Dispatch(GenerationSettingsActions.setOverwriteSoundReferenceAsset, evt.newValue);
            });

            var microphoneChoices = Microphone.devices.ToList();
            m_MicrophonesDropdownField = this.Q<DropdownField>("microphones-dropdown");
            m_MicrophonesDropdownField.choices = microphoneChoices;
            m_MicrophonesDropdownField.RegisterValueChangedCallback(evt =>
            {
                this.Dispatch(SessionActions.setMicrophoneName, evt.newValue);
            });

            this.Use(state => state.SelectOverwriteSoundReferenceAsset(this), OnOverwriteAssetChanged);
            this.Use(state => state.SelectMicrophoneName(), OnMicrophoneNameChanged);
        }

        void OnOverwriteAssetChanged(bool overwrite)
        {
            m_OverwriteAssetToggle.SetValueWithoutNotify(overwrite);
        }

        void OnMicrophoneNameChanged(string microphoneName)
        {
            m_MicrophonesDropdownField.SetValueWithoutNotify(microphoneName);
            m_RecordButton.SetEnabled(!string.IsNullOrEmpty(microphoneName));
        }

        void CreateNewBlankAudioAndReferenceIt()
        {
            var audioClip = AssetUtils.CreateBlankAudioClipSameFolder(this.GetAsset(), " Recording");
            this.Dispatch(GenerationSettingsActions.setSoundReferenceAsset, Unity.AI.Generators.Asset.AssetReferenceExtensions.FromObject(audioClip));
        }

        async Task ToggleRecording()
        {
            if (m_RecordCancellationTokenSource == null)
            {
                await StartRecordingAudio();
            }
            else
            {
                StopRecordingAudio();
            }
        }

        async Task StartRecordingAudio()
        {
            EnableUI(false);
            m_RecordButton.text = "Stop Recording";

            var overwriteAsset = this.GetState().SelectOverwriteSoundReferenceAsset(this);
            var referencedAudio = this.GetState().SelectSoundReferenceAsset(this);

            if (!overwriteAsset || !referencedAudio.IsValid())
                CreateNewBlankAudioAndReferenceIt();

            const int recordClipLengthInSeconds = 60;

            m_RecordCancellationTokenSource?.Cancel();
            m_RecordCancellationTokenSource?.Dispose();
            m_RecordCancellationTokenSource = null;

            m_RecordCancellationTokenSource = new CancellationTokenSource();
            var token = m_RecordCancellationTokenSource.Token;

            var microphoneName = this.GetState().SelectMicrophoneName();

            m_AudioClipRecorded = await Record(token, microphoneName, recordClipLengthInSeconds,time =>
            {
                m_TimeElapsedInMs = time;
            });

            m_RecordButton.text = "Start Recording";
            EnableUI(true);
            await SaveAudioClipInWav(this.GetState().SelectSoundReferenceAsset(this));
        }

        void StopRecordingAudio()
        {
            m_RecordCancellationTokenSource?.Cancel();
            m_RecordCancellationTokenSource?.Dispose();
            m_RecordCancellationTokenSource = null;
        }

        static async Task<AudioClip> Record(CancellationToken token, string microphoneName, int lengthSec, Action<float> timeUpdate = null)
        {
            var recordStopwatch = new Stopwatch();

            Microphone.GetDeviceCaps(microphoneName, out var minFreq, out var maxFreq);
            var freq = (maxFreq + minFreq) / 2;
            freq = freq <= 0 ? 44100 : freq;

            var audioClip = Microphone.Start(microphoneName, false, lengthSec, freq);
            recordStopwatch.Start();

            var shouldStop = false;

            while (!shouldStop)
            {
                shouldStop = recordStopwatch.ElapsedMilliseconds >= lengthSec * 1000;
                try
                {
                    timeUpdate?.Invoke(recordStopwatch.ElapsedMilliseconds);
                }
                catch{/* ignore */}

                if (token.IsCancellationRequested)
                    break;

                await EditorTask.Yield();
            }

            Microphone.End(microphoneName);
            recordStopwatch.Stop();

            return audioClip;
        }

        void EnableUI(bool enable)
        {
            m_MicrophonesDropdownField.SetEnabled(enable);
            m_AudioObjectField.SetEnabled(enable);
        }

        async Task SaveAudioClipInWav(AssetReference assetReference)
        {
            if (!assetReference.IsValid())
            {
                CreateNewBlankAudioAndReferenceIt();
                assetReference = this.GetState().SelectSoundReferenceAsset(this);
            }

            var assetPath = assetReference?.GetPath();
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("Failed to save audio clip. Asset path is null or empty.");
                return;
            }

            await m_AudioClipRecorded.SaveAudioClipToWavAsync(assetPath, new SoundEnvelopeSettings {
                endPosition = m_AudioClipRecorded.GetNormalizedPositionAtTime(m_TimeElapsedInMs / 1000f)
            });
            m_AudioClipRecorded = null;

            // Dispatch the same reference asset because the recording changed its content
            this.Dispatch(GenerationSettingsActions.setSoundReferenceAsset, assetReference);
        }
    }
}
