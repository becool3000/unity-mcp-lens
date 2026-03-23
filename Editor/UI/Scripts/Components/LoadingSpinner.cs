using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// A rotating loading spinner UI element.
    /// </summary>
    [UxmlElement]
    partial class LoadingSpinner : Image
    {
        const float k_RotationSpeedDegreePerSecond = 360f;
        const long k_UpdateIntervalMs = 33; // ~30 FPS
        const float k_DeltaTime = k_UpdateIntervalMs / 1000f;

        IVisualElementScheduledItem m_RotationSchedule;
        float m_CurrentRotation;

        public LoadingSpinner()
        {
            AddToClassList("mui-icon-wait");
            AddToClassList("mui-loading-spinner");
        }

        public void Show()
        {
            style.display = DisplayStyle.Flex;
            StartRotation();
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
            StopRotation();
        }

        void StartRotation()
        {
            if (m_RotationSchedule != null)
                return;

            m_CurrentRotation = 0f;
            m_RotationSchedule = schedule.Execute(UpdateRotation).Every(k_UpdateIntervalMs);
        }

        void StopRotation()
        {
            if (m_RotationSchedule != null)
            {
                m_RotationSchedule.Pause();
                m_RotationSchedule = null;
            }
        }

        void UpdateRotation()
        {
            m_CurrentRotation += k_RotationSpeedDegreePerSecond * k_DeltaTime;
            if (m_CurrentRotation >= 360f)
                m_CurrentRotation -= 360f;

            style.rotate = new Rotate(new Angle(m_CurrentRotation, AngleUnit.Degree));
        }
    }
}
