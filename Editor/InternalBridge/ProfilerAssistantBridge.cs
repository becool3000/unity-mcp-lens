#if !ENABLE_PROFILER_ASSISTANT_INTEGRATION && (UNITY_6000_5_OR_NEWER)
#define ENABLE_PROFILER_ASSISTANT_INTEGRATION
#endif

using System;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEngine;


namespace Unity.AI.Assistant.Bridge.Editor
{
    interface IProxyAskAssistantService : IDisposable
    {
        bool Initialize();

        public struct Context
        {
            public string Payload;

            public string Type;

            public string DisplayName;

            public object Metadata;
        }
        void ShowAskAssistantPopup(Rect parentRect, Context context, string prompt);
    }

    interface IProxyCpuProfilerAskAssistantService : IProxyAskAssistantService
    {
    }

    interface IProxyProjectAuditorAskAssistantService : IProxyAskAssistantService
    {
    }

#if ENABLE_PROFILER_ASSISTANT_INTEGRATION
    // Bridge service to connect Profiler with AI Assistant
    abstract class AskAssistantServiceBase<TProxyService> : IAskAssistantService
        where TProxyService : IProxyAskAssistantService
    {
        protected TProxyService m_ProxyService;

        protected AskAssistantServiceBase()
        {
            var proxyServiceTypes = TypeCache.GetTypesDerivedFrom<TProxyService>();
            if (proxyServiceTypes.Count == 0)
            {
                throw new InvalidOperationException($"No implementation of {typeof(TProxyService).Name} found.");
            }
            else if (proxyServiceTypes.Count > 1)
            {
                throw new InvalidOperationException($"Multiple implementations of {typeof(TProxyService).Name} found.");
            }

            m_ProxyService = (TProxyService)Activator.CreateInstance(proxyServiceTypes[0]);
        }

        public virtual bool Initialize()
        {
            return m_ProxyService.Initialize();
        }

        public virtual void Dispose()
        {
            m_ProxyService?.Dispose();
            m_ProxyService = default;
        }

        public virtual void ShowAskAssistantPopup(Rect parentRect, IAskAssistantService.Context context, string prompt)
        {
            var proxyContext = new IProxyAskAssistantService.Context()
            {
                Payload = context.Payload,
                Type = context.Type,
                DisplayName = context.DisplayName,
                Metadata = context.Metadata
            };
            m_ProxyService.ShowAskAssistantPopup(parentRect, proxyContext, prompt);
        }
    }

    [AskAssistantServiceRole("CPU Profiler Assistant")]
    class CpuProfilerAssistantService : AskAssistantServiceBase<IProxyCpuProfilerAskAssistantService>
    {
    }

    [AskAssistantServiceRole("Project Auditor Assistant")]
    class ProjectAuditorAssistantService : AskAssistantServiceBase<IProxyProjectAuditorAskAssistantService>
    {
    }
#endif

    static class ProfilerMarkerInformationProvider
    {
        public static string GetMarkerInformation(string markerName)
        {
#if ENABLE_PROFILER_ASSISTANT_INTEGRATION
            return MarkersInformationProvider.GetMarkerInfo(markerName);
#else
            return null;
#endif
        }
    }
}
