namespace Unity.AI.Generators.Redux.Thunks
{
    class ThunkCreator<TArg> : ICreateThunk<TArg>
    {
        public ThunkRunner<TArg> Thunk { get; init;}
        public ThunkCreator(ThunkRunner<TArg> thunk) => Thunk = thunk;
        public Thunk Invoke(TArg arg) => api => Thunk(arg, api);
    }
}
