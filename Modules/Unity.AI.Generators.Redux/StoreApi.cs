using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Unity.AI.Generators.Redux
{
    /// <summary>
    /// Client-facing store api.
    /// </summary>
    /// <param name="api">Innermost store api. The dispatch/getstate call the actual store reducer dispatch and actual getstate.</param>
    record StoreApi(IStoreApi api) : IStoreApi
    {
        public Stack<Middleware> middlewares = new();

        public IState State => api.State;
        public Task DispatchAction(object action)
        {
            var stack = new Stack<Middleware>(middlewares);
            var actionHandler = GetNext(stack);
            return actionHandler.Invoke(action);
        }

        /// <summary>
        /// Get the next middleware's action handler.
        /// </summary>
        HandleAction GetNext(Stack<Middleware> stack)
        {
            if (!stack.Any())
                return api.DispatchAction;

            var middleware = stack.Pop();
            var wrapDispatch = middleware(this);
            return wrapDispatch(GetNext(stack));
        }
    }
}
