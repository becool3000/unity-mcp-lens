using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    class PingManipulator : Manipulator
    {
        const string baseUssClassName = "aitk-pingable";

        public const string pingUssClassName = "aitk-ping";

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<TransitionStartEvent>(OnTransitionStart);
            target.AddToClassList(baseUssClassName);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<TransitionCancelEvent>(OnTransitionCancel);
            target.UnregisterCallback<TransitionStartEvent>(OnTransitionStart);
            target.UnregisterCallback<TransitionEndEvent>(OnTransitionEnd);
        }

        void OnTransitionStart(TransitionStartEvent evt)
        {
            target.RegisterCallback<TransitionCancelEvent>(OnTransitionCancel);
            target.RegisterCallback<TransitionEndEvent>(OnTransitionEnd);
        }

        void OnTransitionCancel(TransitionCancelEvent evt)
        {
            Reset();
        }

        void OnTransitionEnd(TransitionEndEvent evt)
        {
            Reset();
        }

        void Reset()
        {
            target.UnregisterCallback<TransitionCancelEvent>(OnTransitionCancel);
            target.UnregisterCallback<TransitionEndEvent>(OnTransitionEnd);
            target.RemoveFromClassList(pingUssClassName);
        }
    }
}
