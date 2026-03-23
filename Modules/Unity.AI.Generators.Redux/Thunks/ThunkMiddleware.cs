using System;

namespace Unity.AI.Generators.Redux.Thunks
{
    static class ThunkMiddleware
    {
        public static Middleware Middleware => api => next => async action =>
        {
            if (action is Thunk thunk)
                thunk(api);
            else if (action is AsyncThunk thunkAsync)
                await thunkAsync(api);
            else
                await next(action);
        };
    }
}
