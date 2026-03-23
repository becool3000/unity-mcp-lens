using UnityEditor.ProjectWindowCallback;

namespace Unity.AI.Generators.UI.Utilities
{
    class DoCreateBlankAsset : EndNameEditAction
    {
        public delegate void ActionHandler(int instanceId, string pathName, string resourceFile);

        public ActionHandler action { get; set; }

        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            action?.Invoke(instanceId, pathName, resourceFile);
        }
    }
}
