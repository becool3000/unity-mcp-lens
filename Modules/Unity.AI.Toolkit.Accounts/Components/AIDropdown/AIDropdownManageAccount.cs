using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts
{
    class AIDropdownManageAccount : VisualElement
    {
        [InitializeOnLoadMethod]
        static void Init() => DropdownExtension.RegisterMainMenuExtension(container => container.Add(new AIDropdownManageAccount()), 4);

        readonly Label m_DataSharing;

        public AIDropdownManageAccount()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/AIDropdown/AIDropdownManageAccount.uxml");
            tree.CloneTree(this);

            this.Q<Label>("manage-account").AddManipulator(new Clickable(AccountLinks.ManageAccount));
            m_DataSharing = this.Q<Label>("data-sharing");

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Account.settings.OnChange += Refresh;
                Account.network.OnChange += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Account.settings.OnChange -= Refresh;
                Account.network.OnChange -= Refresh;
            });
        }

        void Refresh() => m_DataSharing.text = Account.settings.IsDataSharingEnabled ? "Developer Data Sharing On" : "Developer Data Sharing Off";
    }
}
