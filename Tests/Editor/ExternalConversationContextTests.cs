using System;
using System.Reflection;
using NUnit.Framework;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor.Tests
{
    public class ExternalConversationContextTests
    {
        [SetUp]
        public void SetUp()
        {
            ToolExecutionContextFactory.CleanupExternalConversations();
        }

        [TearDown]
        public void TearDown()
        {
            ToolExecutionContextFactory.CleanupExternalConversations();
        }

        [Test]
        public void CreateForExternalCall_ReusesSyntheticConversationPerConnection()
        {
            ToolExecutionContext firstContext;
            ToolExecutionContext secondContext;
            ToolExecutionContext thirdContext;

            using (ToolExecutionContextFactory.BeginExternalExecutionScope("conn-a", "req-1"))
                firstContext = ToolExecutionContextFactory.CreateForExternalCall("Tool.One", null);

            using (ToolExecutionContextFactory.BeginExternalExecutionScope("conn-a", "req-2"))
                secondContext = ToolExecutionContextFactory.CreateForExternalCall("Tool.Two", null);

            using (ToolExecutionContextFactory.BeginExternalExecutionScope("conn-b", "req-3"))
                thirdContext = ToolExecutionContextFactory.CreateForExternalCall("Tool.Three", null);

            Assert.That(firstContext.Conversation, Is.Not.Null);
            Assert.That(secondContext.Conversation, Is.Not.Null);
            Assert.That(thirdContext.Conversation, Is.Not.Null);
            Assert.That(firstContext.Conversation.ConversationId, Is.EqualTo(secondContext.Conversation.ConversationId));
            Assert.That(firstContext.Conversation.ConversationId, Is.Not.EqualTo(thirdContext.Conversation.ConversationId));
            Assert.That(firstContext.Conversation.RequiresExplicitClose, Is.False);
            Assert.That(firstContext.Conversation.IsSynthetic, Is.True);
        }

        [Test]
        public void CreateForExternalCall_WithoutScopeReturnsEphemeralSyntheticConversation()
        {
            var context = ToolExecutionContextFactory.CreateForExternalCall("Tool.One", null);

            Assert.That(context.Conversation, Is.Not.Null);
            Assert.That(context.Conversation.IsSynthetic, Is.True);
            Assert.That(context.Conversation.RequiresExplicitClose, Is.True);

            context.Conversation.Close();
        }

        [Test]
        public void ReleaseExternalConversation_InvokesConnectionClosedHandlers()
        {
            ToolExecutionContext context;
            var wasClosed = false;

            using (ToolExecutionContextFactory.BeginExternalExecutionScope("conn-release", "req-1"))
                context = ToolExecutionContextFactory.CreateForExternalCall("Tool.Release", null);

            context.Conversation.ConnectionClosed += () => wasClosed = true;

            ToolExecutionContextFactory.ReleaseExternalConversation("conn-release");

            Assert.That(wasClosed, Is.True);
        }

        [Test]
        public void ProfilerConversationCache_SupportsScopedExternalContextsAndCleansUpOnRelease()
        {
            object firstCache;
            object secondCache;
            object thirdCache;

            using (ToolExecutionContextFactory.BeginExternalExecutionScope("profiler-conn", "req-1"))
            {
                var context = ToolExecutionContextFactory.CreateForExternalCall("Unity.Profiler.GetOverallGcAllocationsSummary", null);
                firstCache = InvokeProfilerConversationCacheMethod("GetFrameDataCache", context.Conversation);
            }

            using (ToolExecutionContextFactory.BeginExternalExecutionScope("profiler-conn", "req-2"))
            {
                var context = ToolExecutionContextFactory.CreateForExternalCall("Unity.Profiler.GetFrameRangeTopTimeSummary", null);
                secondCache = InvokeProfilerConversationCacheMethod("GetFrameDataCache", context.Conversation);
                InvokeProfilerConversationCacheMethod("ClearFrameDataCache", context.Conversation);
            }

            Assert.That(secondCache, Is.SameAs(firstCache));

            ToolExecutionContextFactory.ReleaseExternalConversation("profiler-conn");

            using (ToolExecutionContextFactory.BeginExternalExecutionScope("profiler-conn", "req-3"))
            {
                var context = ToolExecutionContextFactory.CreateForExternalCall("Unity.Profiler.GetFrameGcAllocationsSummary", null);
                thirdCache = InvokeProfilerConversationCacheMethod("GetFrameDataCache", context.Conversation);
            }

            Assert.That(thirdCache, Is.Not.SameAs(firstCache));
        }

        static object InvokeProfilerConversationCacheMethod(string methodName, ConversationContext conversation)
        {
            var extensionType = Type.GetType(
                "Unity.AI.Assistant.Integrations.Profiler.Editor.ConversationCacheExtension, Unity.AI.Assistant.Integrations.Profiler.Editor");
            Assert.That(extensionType, Is.Not.Null, "Could not locate profiler conversation cache extension type.");

            var method = extensionType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Could not locate profiler conversation cache method '{methodName}'.");

            try
            {
                return method.Invoke(null, new object[] { conversation });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }
    }
}
