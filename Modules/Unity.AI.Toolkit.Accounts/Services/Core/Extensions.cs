using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Services.Core
{
    static class Extensions
    {
        public static Action Use<T>(this Signal<T> signal, Action<T> callback)
        {
            void OnSignalOnOnChange() => callback(signal.Value);
            signal.OnChange += OnSignalOnOnChange;
            callback(signal.Value);

            return () => signal.OnChange -= OnSignalOnOnChange;
        }

        /// <summary>
        /// Returns the current value if non-null, otherwise refreshes and awaits the next non-null value.
        /// </summary>
        public static async Task<T> GetAsync<T>(this Signal<T> signal, CancellationToken ct = default) where T : class
        {
            if (signal.Value != null)
                return signal.Value;

            var tcs = new TaskCompletionSource<T>();
            using var reg = ct.Register(() => tcs.TrySetCanceled());
            Action handler = null;
            handler = () =>
            {
                if (signal.Value == null) return;
                signal.OnChange -= handler;
                tcs.TrySetResult(signal.Value);
            };
            signal.OnChange += handler;
            signal.Refresh();

            try { return await tcs.Task; }
            finally { signal.OnChange -= handler; }
        }

        internal static void OnShow(VisualElement element)
        {
            try
            {
                foreach (var item in DropdownExtension.onShow.OrderBy(item => item.order))
                    item.callback.Invoke(element);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal static void OnExtend(VisualElement element)
        {
            try
            {
                foreach (var item in DropdownExtension.onExtend.OrderBy(item => item.order))
                    item.callback.Invoke(element);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal static void OnExtendGeneral(VisualElement element)
        {
            try
            {
                foreach (var item in DropdownExtension.onExtendMain.OrderBy(item => item.order))
                    item.callback.Invoke(element);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
