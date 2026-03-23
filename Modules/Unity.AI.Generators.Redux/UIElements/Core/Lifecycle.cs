using System;
using System.Threading.Tasks;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Generators.UIElements.Core
{
    /// <summary>
    /// An object that automatically generate an Unsubscribe method from a subscriber that produces an Unsubscribe.
    /// </summary>
    class LifecycleAsync
    {
        readonly SubscribeAsync m_Subscribe;
        Unsubscribe m_Unsubscribe;

        public LifecycleAsync(SubscribeAsync subscribe)
        {
            m_Subscribe = subscribe;
        }

        async Task SubscribeAsync()
        {
            m_Unsubscribe = await m_Subscribe();
        }

        public void Subscribe() => _ = SubscribeAsync();

        public bool Unsubscribe()
        {
            var result = m_Unsubscribe?.Invoke() ?? false;
            m_Unsubscribe = null;
            return result;
        }

        public void UnsubscribeAction() => Unsubscribe();
    }

    class Lifecycle : LifecycleAsync
    {
        public Lifecycle(Subscribe subscribe) : base(() => Task.FromResult(subscribe())) { }
    }
}
