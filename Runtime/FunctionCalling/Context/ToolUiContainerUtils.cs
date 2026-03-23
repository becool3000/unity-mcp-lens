using System;

namespace Unity.AI.Assistant.FunctionCalling
{
    static class ToolUiContainerUtils
    {
        public static IDisposable PushElementScoped<TOutput>(this IToolUiContainer container, ToolExecutionContext.CallInfo callInfo, IUserInteraction<TOutput> userInteraction)
        {
            if (userInteraction == null)
                return EmptyDisposable.Empty;

            container.PushElement(callInfo, userInteraction);
            return new PopOnDispose<TOutput>(callInfo, container, userInteraction);
        }

        class PopOnDispose<TOutput> : IDisposable
        {
            readonly IToolUiContainer m_Owner;
            readonly IUserInteraction<TOutput> m_UserInteraction;
            bool m_Disposed;
            ToolExecutionContext.CallInfo m_CallInfo;

            public PopOnDispose(ToolExecutionContext.CallInfo callInfo, IToolUiContainer owner, IUserInteraction<TOutput> userInteraction)
            {
                m_CallInfo = callInfo;
                m_Owner = owner;
                m_UserInteraction = userInteraction;
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                m_Owner.PopElement(m_CallInfo, m_UserInteraction);
            }
        }

        class EmptyDisposable : IDisposable
        {
            public static readonly IDisposable Empty = new EmptyDisposable();
            public void Dispose() { }
        }
    }
}
