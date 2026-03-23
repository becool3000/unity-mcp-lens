using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Integrations.Sample.Editor
{
    class SampleWindow : EditorWindow
    {
        Button m_HeadlessButton;
        Button m_PromptThenRunButton;
        Button m_PromptThenRunSkillButton;

        public SampleWindow()
        {
            titleContent = new GUIContent("API Sample");
            minSize = new Vector2(200, 150);
            maxSize = new Vector2(200, 150);
        }

        public void CreateGUI()
        {
            // Root container style
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.style.justifyContent = Justify.SpaceAround;
            root.style.alignItems = Align.Center;
            root.style.paddingTop = 20;
            root.style.paddingBottom = 20;

            // Button 1 - Run Headless
            m_HeadlessButton = new Button(() => _ = RunHeadless()) { text = "Run Headless" };
            m_HeadlessButton.style.width = 150;
            m_HeadlessButton.style.height = 30;
            root.Add(m_HeadlessButton);

            // Button 2 - Run with UI
            var withUiButton = new Button(RunWithUI) { text = "Run with UI" };
            withUiButton.style.width = 150;
            withUiButton.style.height = 30;
            root.Add(withUiButton);

            // Button 3 - Prompt then Run
            m_PromptThenRunButton = new Button(PromptThenRun) { text = "Prompt then Run" };
            m_PromptThenRunButton.style.width = 150;
            m_PromptThenRunButton.style.height = 30;
            root.Add(m_PromptThenRunButton);

            // Button 4 - Prompt then Run
            m_PromptThenRunSkillButton = new Button(PromptThenRunSkill) { text = "Prompt then Run Skill" };
            m_PromptThenRunSkillButton.style.width = 150;
            m_PromptThenRunSkillButton.style.height = 30;
            root.Add(m_PromptThenRunSkillButton);
        }

        async Task RunHeadless()
        {
            m_HeadlessButton.SetEnabled(false);
            var output = await ApiExample.RunHeadless();
            m_HeadlessButton.SetEnabled(true);
            EditorUtility.DisplayDialog(
                "Assistant Output (Headless)",
                output,
                "OK"
            );
        }

        void RunWithUI()
        {
            _ = ApiExample.RunWithUI();
        }

        void PromptThenRun()
        {
            _ = ApiExample.PromptThenRun(m_PromptThenRunButton);
        }

        void PromptThenRunSkill()
        {
            _ = ApiExample.PromptThenRunSkill(m_PromptThenRunSkillButton);
        }
    }
}
