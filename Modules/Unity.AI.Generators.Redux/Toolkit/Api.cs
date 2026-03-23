using System;

namespace Unity.AI.Generators.Redux.Toolkit
{
    class Api : IDisposable
    {
        internal const string DefaultSlice = "api";

        internal IReduxAdapter reduxAdapter;
        internal ApiOptions options;
        internal Store store => options.store;

        public Api(Store store) : this(new ApiOptions(store)) {}
        public Api(ApiOptions options)
        {
            reduxAdapter = new ReduxAdapter();
            reduxAdapter.Init(options);
            this.options = options;
            internalActions = new(options.slice);
        }

        internal ApiActions internalActions;

        public void Dispose() => reduxAdapter.Dispose();
    }
}
