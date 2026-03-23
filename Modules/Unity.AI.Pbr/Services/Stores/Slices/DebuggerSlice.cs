using System;
using Newtonsoft.Json;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Generators.Redux;
using UnityEngine;

namespace Unity.AI.Pbr.Services.Stores.Slices
{
    static class DebuggerSlice
    {
        public static void Create(Store store) => store.CreateSlice(
            DebuggerActions.slice,
            new DebuggerState(),
            reducers => reducers
                .Add(DebuggerActions.setRecording, (state, payload) => state.record = payload)
                .Add(DebuggerActions.init).With((state, payload) => payload with {}),
            extraReducers => extraReducers
                .AddMatcher(action => action.type != AppActions.init.type, (state, action) =>
                {
                    state.info.action = action;
                    state.info.json = JsonConvert.SerializeObject(action, Formatting.Indented);
                    state.info.tick++;
                }),
            state => state with
            {
                info = state.info with {}
            });
    }
}

