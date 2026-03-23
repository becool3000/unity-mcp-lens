using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts
{
    class AIDropdownCheckUpdates : VisualElement
    {
        [InitializeOnLoadMethod]
        static void Init() => DropdownExtension.RegisterMainMenuExtension(container => container.Add(new AIDropdownCheckUpdates()), 8);

        public AIDropdownCheckUpdates()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/AIDropdown/AIDropdownCheckUpdates.uxml");
            tree.CloneTree(this);

            this.Q<Label>("check-updates").AddManipulator(new Clickable(AccountLinks.OpenInPackageManager));
        }
    }
}
