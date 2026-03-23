using System;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    class SpinnerManipulator : Manipulator
    {
        IVisualElementScheduledItem m_Scheduler;
        bool m_IsSpinning = false;

        protected override void RegisterCallbacksOnTarget() {}

        protected override void UnregisterCallbacksFromTarget() => Stop();

        public void Start()
        {
            if (target == null)
                return;

            target.SetShown(true);

            if (m_IsSpinning)
                return;

            m_IsSpinning = true;

            m_Scheduler = target.schedule.Execute(UpdateRotation);
            var interval = Application.platform == RuntimePlatform.OSXEditor ? 100 : 16;
            m_Scheduler.Every(interval); // 10fps on macOS editor, ~60fps elsewhere
        }

        public void Stop()
        {
            if (target == null)
                return;

            target.SetShown(false);

            if (!m_IsSpinning)
                return;

            m_Scheduler?.Pause();
            m_Scheduler = null;

            m_IsSpinning = false;
        }

        public bool IsSpinning => m_IsSpinning;

        void UpdateRotation()
        {
            if (target == null || !m_IsSpinning)
                return;

            var milliseconds = DateTime.Now.Millisecond;
            var degrees = milliseconds * 360f / 1000f;
            target.style.rotate = new Rotate(new Angle(degrees, AngleUnit.Degree));
        }
    }
}
