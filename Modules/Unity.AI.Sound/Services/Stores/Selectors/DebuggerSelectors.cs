
using Unity.AI.Sound.Services.Stores.Actions;
using Unity.AI.Sound.Services.Stores.Slices;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Sound.Services.Stores.Selectors
{
    static partial class Selectors
    {
        public static DebuggerState SelectDebugger(this IState state) => state.Get<DebuggerState>(DebuggerActions.slice);
        public static bool SelectRecording(this IState state) => state.SelectDebugger()?.record ?? false;
    }
}
