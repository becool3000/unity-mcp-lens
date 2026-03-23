using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Services.Utilities
{
    class PlayManipulator : Clickable
    {
        CancellationTokenSource m_CancellationTokenSource;

        readonly Func<SoundEnvelopeSettings> m_Envelope;
        readonly Func<bool> m_IsLooping;
        readonly Func<AudioClip> m_Asset;

        public Action<float> timeUpdate;

        public PlayManipulator(Func<SoundEnvelopeSettings> envelope, Func<AudioClip> asset, Func<bool> isLooping)
            : base(() => { })
        {
            m_Envelope = envelope;
            m_Asset = asset;
            m_IsLooping = isLooping;
        }

        public void Cancel()
        {
            m_CancellationTokenSource?.Cancel();
            m_CancellationTokenSource?.Dispose();
            m_CancellationTokenSource = null;
        }

        protected override void ProcessDownEvent(EventBase evt, Vector2 localPosition, int pointerId)
        {
            base.ProcessDownEvent(evt, localPosition, pointerId);
            _ = Play();
        }
        
        async Task Play()
        {
            var asset = m_Asset();
            
            try
            {
                m_CancellationTokenSource?.Cancel();
                m_CancellationTokenSource?.Dispose();
                m_CancellationTokenSource = null;
                if (target.IsSelected())
                    return;

                target.SetSelected();
                m_CancellationTokenSource = new CancellationTokenSource();
                var token = m_CancellationTokenSource.Token;
                var once = true;
                while ((once || m_IsLooping()) && !token.IsCancellationRequested)
                {
                    once = false;
                    await asset.Play(token, timeUpdate, m_Envelope?.Invoke());
                }
            }
            finally
            {
                target.SetSelected(false);
            }
        }
    }
}
