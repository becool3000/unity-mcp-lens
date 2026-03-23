
using Unity.AI.Mesh.Services.Stores.Actions;
using Unity.AI.Mesh.Services.Stores.Slices;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Mesh.Services.Stores.Selectors
{
    static partial class Selectors
    {
        public static DebuggerState SelectDebugger(this IState state) => state.Get<DebuggerState>(DebuggerActions.slice);
        public static bool SelectRecording(this IState state) => state.SelectDebugger()?.record ?? false;
    }
}
