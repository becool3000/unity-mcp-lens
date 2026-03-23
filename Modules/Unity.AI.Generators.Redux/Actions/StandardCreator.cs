namespace Unity.AI.Generators.Redux
{
    delegate TAction ActionCreator<TAction>();
    delegate TAction ActionCreator<TAction, TArgs>(TArgs args);

    /// <summary>
    /// Action creator that takes an action and returns it with a type
    /// </summary>
    record CreatorWithAction<TAction>(string type, PrepareAction<TAction, TAction> prepare = null) :
        IStandardCreatorWithArgs<TAction, TAction>, IPrepareAction<TAction, TAction> where TAction: StandardAction
    {
        public TAction Invoke(TAction args) => (prepare?.Invoke(args) ?? args) with {type = type};
    }

    /// <summary>
    /// Action creator where the arguments given to create the action are the same as the payload.
    /// </summary>
    record StandardCreator<TAction, TPayload>(string type, PrepareAction<TPayload, TPayload> prepare = null) :
        IStandardCreator<TAction, TPayload, TPayload> where TAction: StandardAction<TPayload>
    {
        public ActionCreator<TAction, TPayload> creator {get; set;}

        public TAction Invoke(TPayload args) => creator(args) with {type = type, payload = prepare == null ? args : prepare(args)};
    }

    record StandardCreator<TAction, TPayload, TArgs>(string type, PrepareAction<TArgs, TPayload> prepare = null) :
        IStandardCreator<TAction, TPayload, TArgs> where TAction: StandardAction<TPayload>
    {
        public ActionCreator<TAction, TArgs> creator {get; set;}
        public TAction Invoke(TArgs args) => creator(args) with {type = type, payload = prepare == null ? default : prepare(args)};
    }

    record StandardCreatorWithArgs<TAction, TArgs>(string type) :
        IStandardCreatorWithArgs<TAction, TArgs> where TAction: StandardAction
    {
        public ActionCreator<TAction, TArgs> creator {get; set;}
        public TAction Invoke(TArgs args) => creator(args) with {type = type};
    }
}
