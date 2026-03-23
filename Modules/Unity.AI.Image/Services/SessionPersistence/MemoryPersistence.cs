using System;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Image.Services.SessionPersistence
{
    [Serializable]
    static class MemoryPersistence
    {
        public static Unsubscribe Persist<T>(Store store, Creator<T> initAction, Selector<T> selector) where T: class, new()
        {
            store.Dispatch(initAction, ObjectPersistence.instance.Get<T>(initAction.type));
            var unsubscribe = store.Subscribe(state => ObjectPersistence.instance.Set(initAction.type, selector(state)));
            store.OnDispose += () => unsubscribe?.Invoke();
            return unsubscribe;
        }
    }
}
