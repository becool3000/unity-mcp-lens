using System;

namespace Unity.AI.Generators.Redux
{
    static class StoreApiExtensions
    {
        public static void Dispatch(this IStoreApi api, object obj) =>
            api.DispatchAction(obj);
        public static void Dispatch(this IStoreApi api, string type) =>
            api.DispatchAction(new StandardAction(type));
        public static void Dispatch<TPayload>(this IStoreApi api, string type, TPayload payload) =>
            api.DispatchAction(new StandardAction<TPayload>(type, payload));
        public static void Dispatch<TPayload>(this IStoreApi api, StandardAction<TPayload> action, TPayload payload) =>
            api.DispatchAction(action with {payload = payload});
        public static void Dispatch(this IStoreApi api, ICreateAction actionCreator) =>
            api.DispatchAction(actionCreator.Invoke());
        public static void Dispatch<TArgs>(this IStoreApi api, ICreateAction<TArgs> actionCreator, TArgs args) =>
            api.DispatchAction(actionCreator.Invoke(args));
        public static void Dispatch<TAction, TArgs>(this IStoreApi api, ICreateAction<TAction, TArgs> actionCreator, TArgs args) =>
            api.DispatchAction(actionCreator.Invoke(args));
        public static void Dispatch(this IStoreApi store, Delegate @delegate) => store.DispatchAction(@delegate);
        public static void Dispatch<TAction, TArg>(this IStoreApi api, ActionCreator<TAction, TArg> creator, TArg arg) => 
            api.DispatchAction(creator(arg));
        

        public static bool SliceExists(this IStoreApi store, string slice) => store.State.ContainsKey(slice);
    }
}
