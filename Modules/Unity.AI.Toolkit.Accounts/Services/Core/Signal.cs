using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Services.Core
{
    class Signal<T>
    {
        readonly Action m_OnRefresh;
        readonly Comparer<T> m_Compare;
        readonly IProxy<T> m_Value;
        readonly Action m_OnChangeMethod;

        public Signal(IProxy<T> value, Action onRefresh, Action onChange, Comparer<T> compare = null, Action onChangeMethod = null)
        {
            m_Value = value;
            m_OnRefresh = onRefresh;
            OnChange += onChange;
            m_Compare = compare ?? EqualityComparer<T>.Default.Equals;
            m_OnChangeMethod = onChangeMethod;
            m_OnChangeMethod ??= () => OnChange?.Invoke();
        }

        public Action OnChange;

        public T Value
        {
            get => m_Value.Value;
            set
            {
                var changed = !m_Compare(Value, value);
                m_Value.Value = value;
                if (changed)
                    m_OnChangeMethod();
            }
        }

        public void Refresh() => m_OnRefresh?.Invoke();

        public void SetValueWithoutNotify(T value) => m_Value.Value = value;
    }
}
