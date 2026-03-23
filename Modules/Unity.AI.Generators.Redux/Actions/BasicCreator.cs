using System;

namespace Unity.AI.Generators.Redux
{
    /// <summary>
    /// Configurable Action creator
    /// </summary>
    record BasicCreator<TAction, TArgs>(string type, ActionCreator<TAction, TArgs> creator = null) :
        ICreateAction<TAction, TArgs> where TAction: StandardAction
    {
        public ActionCreator<TAction, TArgs> creator {get; set;} = creator;

        public TAction Invoke(TArgs args) => creator(args) with {type = type};
    }

    record BasicCreator<TArgs>(string type, ActionCreator<StandardAction, TArgs> creator = null) :
        BasicCreator<StandardAction, TArgs>(type, creator) {}
}
