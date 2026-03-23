using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class AccountLoadDelayedBanner : BasicBannerContent
    {
        Button m_Retry;
        DropdownLoading m_DropdownLoading;
        Image m_WarningIcon;
        RichLabel m_RichLabel;

        public AccountLoadDelayedBanner()
        {
            var message = $"Unable to load account information from {AccountApi.selectedEnvironment}";
            var loadingMessage = "Loading account information";

            Init();
            content.AddToClassList("banner-content");
            m_WarningIcon = CreateWarningIcon();
            m_RichLabel = new RichLabel(message, null);
            m_DropdownLoading = new DropdownLoading(loadingMessage);
            m_Retry = new Button(() => _ = Retry()) { text = "Retry" };

            RegisterCallback<AttachToPanelEvent>(evt => _ = Retry());
        }

        async Task Retry()
        {
            content.Clear();
            content.Add(m_DropdownLoading);

            await EditorTask.RunOnMainThread(Account.settings.RefreshSettings);

            content.Clear();
            content.Add(m_WarningIcon);
            content.Add(m_RichLabel);
            content.Add(m_Retry);
        }
    }
}
