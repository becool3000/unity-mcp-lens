using System;
using UnityEngine;

namespace Unity.AI.Generators.Redux
{
    /// <summary>
    /// Standard action format.
    ///
    /// Since most actions end up having very similar requirements, using these property names as a standard is good practice.
    ///
    /// Mostly the standard format is {type, payload, context, meta}, where we have records that have some or all of these members.
    /// </summary>
    [Serializable]
    record StandardAction(string type = "") : IAction
    {
        /// <summary>
        /// The type of the action.
        /// </summary>
        [field: SerializeField] public string type { get; set; } = type;

        public StandardAction() : this("") {}
    }

    /// <summary>
    /// Action With payload
    /// </summary>
    [Serializable]
    record StandardAction<TPayload>(string type = "", TPayload payload = default) : StandardAction(type)
    {
        [field: SerializeReference] public TPayload payload { get; set; } = payload;        // Only present to allow serialization and viewing in debugger

        public StandardAction(TPayload payload) : this("", payload) { }
    }

    /// <summary>
    /// Action With payload and context
    /// </summary>
    [Serializable]
    record StandardAction<TPayload, TContext>(string type = "", TPayload payload = default, TContext context = default) :
        StandardAction<TPayload>(type, payload), IContext<TContext>
    {
        [field: SerializeReference] public TContext context { get; set; } = context;
    }

    /// <summary>
    /// Action With payload, context and meta
    /// </summary>
    [Serializable]
    record StandardAction<TPayload, TContext, TMeta>(string type = "", TPayload payload = default, TContext context = default, TMeta meta = default) :
        StandardAction<TPayload, TContext>(type, payload, context)
    {
        [field: SerializeReference] public TMeta meta { get; set; } = meta;
    }
}
