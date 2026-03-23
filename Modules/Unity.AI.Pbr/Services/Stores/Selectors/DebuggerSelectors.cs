
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.Slices;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Pbr.Services.Stores.Selectors
{
    static partial class Selectors
    {
        public static DebuggerState SelectDebugger(this IState state) => state.Get<DebuggerState>(DebuggerActions.slice);
        public static bool SelectRecording(this IState state) => state.SelectDebugger()?.record ?? false;
    }
}
