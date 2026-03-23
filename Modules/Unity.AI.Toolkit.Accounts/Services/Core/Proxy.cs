using System;

namespace Unity.AI.Toolkit.Accounts.Services.Core
{
    class Proxy<T> : IProxy<T>
    {
        protected Func<T> Get { get; init; }
        protected Action<T> Set { get; init; }

        public T Value { get => Get(); set => Set(value); }

        public Proxy(Func<T> get, Action<T> set)
        {
            Get = get;
            Set = set;
        }
    }

    class ValueProxy<T> : Proxy<T>
    {
        T m_Value;

        public ValueProxy(Func<T> get = null, Action<T> set = null) : base(get, set)
        {
            if (get == null)
                Get = () => m_Value;
            if (set == null)
                Set = val => m_Value = val;
        }
    }
}
