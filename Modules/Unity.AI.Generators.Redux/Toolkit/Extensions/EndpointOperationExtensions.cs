using System;
using System.Threading.Tasks;

namespace Unity.AI.Generators.Redux.Toolkit
{
    static class EndpointOperationExtensions
    {
        /// <summary>
        /// Listens to an operation's `result` changes.
        /// </summary>
        public static async Task<UseEndpointResult<TResult>> UseResult<TResult>(
            this EndpointOperation<TResult> operation, Action<EndpointResult<TResult>> callback)
        {
            operation.Subscribe();

            var unsubscribe = UIElements.Use.Selector(
                state => state.SelectEndpointResult<TResult>(operation.cacheInfo),
                callback,
                operation.api.store);

            return new(await operation, () =>
            {
                operation.Unsubscribe();
                return unsubscribe();
            });
        }

        public static Task<UseEndpointResult<TResult>> UseResult<TResult>(this EndpointOperation<TResult> operation, Func<EndpointResult<TResult>, bool> filter, Action<EndpointResult<TResult>> callback) =>
            operation.UseResult(result =>
            {
                if (!filter(result))
                    return;
                callback(result);
            });

        public static Task<UseEndpointResult<TResult>> UseDone<TResult>(this EndpointOperation<TResult> operation, Action<EndpointResult<TResult>> callback) =>
            operation.UseResult(result => result.isDone, callback);

        /// <summary>
        /// Only get notified on successful result.
        /// </summary>
        public static Task<UseEndpointResult<TResult>> Use<TResult>(this EndpointOperation<TResult> operation, Action<TResult> callback) =>
            operation.UseResult(result => result?.isSuccess ?? false, result => callback(result.payload));
    }
}
