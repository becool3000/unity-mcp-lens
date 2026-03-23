using System;
using System.Threading.Tasks;
using AiEditorToolsSdk.Domain.Abstractions.Services;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;

namespace Unity.AI.Generators.Sdk
{
    /// <summary>
    /// Very safe and very thread-safe implementation of <see cref="ITraceIdProvider"/>.
    /// This class captures the trace id at construction and always returns the same value.
    /// Use this when stability is critical and you want to avoid any risk of race conditions or session id changes.
    /// </summary>
    class PreCapturedTraceIdProvider : ITraceIdProvider
    {
        readonly AssetReference m_AssetReference;

        readonly long m_Value = EditorAnalyticsSessionInfo.id;

        public PreCapturedTraceIdProvider(AssetReference asset) => m_AssetReference = asset;

        public Task<string> GetTraceId()
        {
            return Task.FromResult($"{m_AssetReference.guid}&{m_Value}");
        }
    }

    class TraceIdProvider : ITraceIdProvider
    {
        readonly AssetReference m_AssetReference;

        public TraceIdProvider(AssetReference asset) => m_AssetReference = asset;

        public async Task<string> GetTraceId()
        {
            var id = await EditorTask.RunOnMainThread(() => Task.FromResult(EditorAnalyticsSessionInfo.id));
            return $"{m_AssetReference.guid}&{id}";
        }
    }
}
