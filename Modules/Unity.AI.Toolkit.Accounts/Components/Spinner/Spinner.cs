using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class Spinner : VisualElement
    {
        const string k_ClassName = "stopped";
        float m_Angle;
        Image m_Spinner;
        ValueAnimation<Quaternion> m_Animation;

        public Spinner()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/Spinner/Spinner.uxml");
            tree.CloneTree(this);

            m_Spinner = this.Q<Image>(className: "spinner");
            Start();
        }

        public void Start()
        {
            m_Spinner.RemoveFromClassList(k_ClassName);
            Loop(m_Spinner, t => Easing.InPower(t, 2));
        }

        public void Stop()
        {
            m_Spinner.AddToClassList(k_ClassName);
            m_Animation?.Stop();
        }

        void Loop(VisualElement element, Func<float, float> easing = null)
        {
            easing ??= Easing.Linear;

            m_Angle += 180;
            m_Animation = element.experimental.animation.Rotation(Quaternion.Euler(0, 0, -m_Angle), 500).Ease(easing).OnCompleted(() =>
            {
                if (m_Angle >= 359.9f)
                    m_Angle = 0;

                Loop(element);
            });
        }
    }
}
