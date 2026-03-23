using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts
{
    class AIDropdownViewDocumentation : VisualElement
    {
        [InitializeOnLoadMethod]
        static void Init() => DropdownExtension.RegisterMainMenuExtension(container => container.Add(new AIDropdownViewDocumentation()),1); // what's new is '0'

        public AIDropdownViewDocumentation()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/AIDropdown/AIDropdownViewDocumentation.uxml");
            tree.CloneTree(this);

            this.Q<Label>("view-documentation").AddManipulator(new Clickable(AccountLinks.ViewDocumentation));
        }
    }
}
