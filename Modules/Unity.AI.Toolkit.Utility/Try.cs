using System;
using System.ComponentModel;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.AI.Toolkit.Utility
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    static class Try
    {
        public static void Safely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public static async Task Safely(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public static async Task<TResult> Safely<TResult>(Task<TResult> task)
        {
            try
            {
                return await task;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            return default;
        }
    }
}