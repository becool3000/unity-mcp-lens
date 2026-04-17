using System.Collections.Generic;
using UnityEditor;

namespace Becool.UnityMcpLens.Editor.Utils
{
    static class EditorStabilityUtility
    {
        public static bool IsStable() => GetBlockingReasons().Count == 0;

        public static List<string> GetBlockingReasons()
        {
            var reasons = new List<string>();

            if (EditorApplication.isCompiling)
                reasons.Add("compiling");

            if (EditorApplication.isUpdating)
                reasons.Add("updating");

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                reasons.Add("play_transition");

            if (BuildPipeline.isBuildingPlayer)
                reasons.Add("building_player");

            return reasons;
        }
    }
}
