using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.Redux.Thunks
{
    static class AsyncThunkExtensions
    {
        /// <summary>
        /// IStoreApi extensions.
        ///
        /// Ideally would be mostly handled in the middleware, but it's not so straightforward to do because of C# types and generics.
        /// </summary>

        // Adding `Async` suffix until we figure out why otherwise another overload gets priority.
        public static Task DispatchAsync(this IStoreApi api, AsyncThunk asyncThunk) => api.DispatchAction(asyncThunk);

        public static void Dispatch(this IStoreApi api, Thunk thunk) => api.DispatchAction(thunk);
        public static void Dispatch<TArg>(this IStoreApi api, ICreateThunk<TArg> thunkCreator, TArg arg) =>
            api.DispatchAction(thunkCreator.Invoke(arg));
        public static Task Dispatch<TArg>(this IStoreApi api, ICreateAsyncThunk<TArg> thunkCreator, TArg arg, CancellationToken token = default) =>
            api.DispatchAction(thunkCreator.Invoke(arg, token));
        public static Task Dispatch(this IStoreApi api, ICreateAsyncThunk thunkCreator, CancellationToken token) =>
            api.DispatchAction(thunkCreator.Invoke(token));
        public static Task Dispatch(this IStoreApi api, ICreateAsyncThunk thunkCreator) =>
            api.DispatchAction(thunkCreator.Invoke());

        /// <summary>
        /// VisualElement passthrough for IStoreApi
        /// </summary>
        public static Task Dispatch(this VisualElement element, ICreateAsyncThunk thunkCreator) =>
            element.GetStoreApi().Dispatch(thunkCreator, default);
        public static Task Dispatch<TArg>(this VisualElement element, ICreateAsyncThunk<TArg> thunkCreator, TArg arg) =>
            element.GetStoreApi().Dispatch(thunkCreator, arg);
        public static void Dispatch<TArg>(this VisualElement element, ThunkCreator<TArg> thunkCreator, TArg arg) =>
            element.GetStoreApi().Dispatch(thunkCreator, arg);
    }
}
