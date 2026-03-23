using Unity.AI.Assistant.Editor.ServerCompatibility;
using Unity.AI.Assistant.Editor;
using Unity.AI.Toolkit.Accounts.Components;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.SessionBanner
{
    /// <summary>
    /// Session top banners.
    ///
    /// Acts as a state machine where `CurrentView` return the view that should currently be showed.
    /// </summary>
    [UxmlElement]
    partial class SessionBanner : AssistantSessionStatusBanner
    {
        AssistantInsufficientPointsBanner m_InsufficientPointsBanner;
        AcpSessionStatusBannerProvider m_AcpBannerProvider;
        UpdateAvailableBanner m_UpdateAvailableBanner;
        bool m_IsAttached;

        public SessionBanner()
        {
            NotificationsState.instance.hideCompatibility = false;
            this.AddManipulator(new ServerCompatibilityChanges(Refresh));
            this.AddManipulator(new PointsBalanceChanges(Refresh));
            this.AddManipulator(new ProviderChanges(OnProviderChanged));
            this.AddManipulator(new PackageUpdateStateChanges(OnPackageUpdateStateChanged));
            AssistantEditorPreferences.ShowPackageUpdateBannerChanged += OnShowPackageUpdateBannerChanged;
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            m_IsAttached = true;

            // Subscribe directly to ready state changes to ensure banner updates
            ProviderStateObserver.OnReadyStateChanged += OnReadyStateChanged;

            if (!ProviderStateObserver.IsUnityProvider)
            {
                m_AcpBannerProvider ??= new AcpSessionStatusBannerProvider();
                m_AcpBannerProvider.Attach();
                m_AcpBannerProvider.OnChange += Refresh;
            }
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            m_IsAttached = false;

            ProviderStateObserver.OnReadyStateChanged -= OnReadyStateChanged;

            if (m_AcpBannerProvider != null)
            {
                m_AcpBannerProvider.OnChange -= Refresh;
                m_AcpBannerProvider.Detach();
            }
        }

        void OnReadyStateChanged(ProviderStateObserver.ProviderReadyState state, string error)
        {
            Refresh();
        }

        void OnShowPackageUpdateBannerChanged(bool newValue)
        {
            Refresh();
        }

        void OnPackageUpdateStateChanged()
        {
            // Clear the cached banner so it gets recreated with updated state
            m_UpdateAvailableBanner = null;
            Refresh();
        }

        protected override VisualElement CurrentView()
        {
            var view = ProviderStateObserver.IsUnityProvider
                ? GetUnityProviderBanner()
                : GetAcpProviderBanner();

            // General banners apply regardless of provider
            view ??= GetGeneralBanner();

            EnableInClassList("empty", view == null);
            return view;
        }

        VisualElement GetUnityProviderBanner()
        {
            var view = base.CurrentView();

            if (view == null)
                return GetAssistantBanner();

            if (view is LowPointsBanner && !Account.pointsBalance.CanAfford(AssistantConstants.ChatPreAuthorizePoints))
                return m_InsufficientPointsBanner ??= new AssistantInsufficientPointsBanner();

            return view;
        }

        VisualElement GetAcpProviderBanner()
        {
            return m_AcpBannerProvider?.GetCurrentView();
        }

        VisualElement GetAssistantBanner()
        {
            if (ServerCompatibility.ServerCompatibility.Status == ServerCompatibility.ServerCompatibility.CompatibilityStatus.Unsupported)
                return new ServerCompatibilityNotSupportedBanner();

            if (ServerCompatibility.ServerCompatibility.Status == ServerCompatibility.ServerCompatibility.CompatibilityStatus.Deprecated &&
                !NotificationsState.instance.hideCompatibility)
                return new ServerCompatibilityDeprecatedNotificationView(Dismiss);

            if (!Account.pointsBalance.CanAfford(AssistantConstants.ChatPreAuthorizePoints))
                return m_InsufficientPointsBanner ??= new AssistantInsufficientPointsBanner();

            return null;
        }

        VisualElement GetGeneralBanner()
        {
            if (PackageUpdateState.instance.updateAvailable && !PackageUpdateState.instance.dismissed)
            {
                var current = PackageUpdateState.instance.currentVersion;
                var latest = PackageUpdateState.instance.latestVersion;
                return m_UpdateAvailableBanner ??= new UpdateAvailableBanner(current, latest,
                    () => {
                        if (!string.IsNullOrEmpty(latest))
                            _ = AssistantPackageAutoUpdater.UpdatePackage(latest);
                    });
            }

            return null;
        }

        void OnProviderChanged()
        {
            if (m_IsAttached)
            {
                // Detach old provider if it was attached
                if (m_AcpBannerProvider != null)
                {
                    m_AcpBannerProvider.OnChange -= Refresh;
                    m_AcpBannerProvider.Detach();
                }

                // Attach new provider if switching to ACP
                if (!ProviderStateObserver.IsUnityProvider)
                {
                    m_AcpBannerProvider ??= new AcpSessionStatusBannerProvider();
                    m_AcpBannerProvider.Attach();
                    m_AcpBannerProvider.OnChange += Refresh;
                }
            }

            Refresh();
        }

        void Dismiss()
        {
            Clear();
            Refresh();
        }
    }
}
