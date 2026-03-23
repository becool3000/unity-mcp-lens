using System;
using Unity.AI.Toolkit.Accounts.Services.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace Unity.AI.Toolkit.Accounts.Services.Core
{
    [Serializable]
    class AccountPersistence : ScriptableSingleton<AccountPersistence>
    {
        [SerializeReference]
        public SettingsRecord settings;
        [SerializeReference]
        public PointsBalanceRecord pointsBalance;
        public SignInStatus signInStatus;
        public bool networkAvailable;
        public ProjectStatus isCloudConnected;
        public bool legalAgreement = true;
        public bool regionAvailable = true;
        public bool packagesSupported = true;

        public static Proxy<SettingsRecord> SettingsProxy = new(() => instance.settings, value => instance.settings = value);
        public static Proxy<PointsBalanceRecord> PointsBalanceProxy = new(() => instance.pointsBalance, value => instance.pointsBalance = value);
        public static Proxy<SignInStatus> SignInStatusProxy = new(() => instance.signInStatus, value => instance.signInStatus = value);
        public static Proxy<bool> NetworkAvailableProxy = new(() => instance.networkAvailable, value => instance.networkAvailable = value);
        public static Proxy<ProjectStatus> CloudConnectedProxy = new(() => instance.isCloudConnected, value => instance.isCloudConnected = value);
        public static Proxy<bool> LegalAgreementProxy = new(() => instance.legalAgreement, value => instance.legalAgreement = value);
        public static Proxy<bool> RegionAvailabilityProxy = new(() => instance.regionAvailable, value => instance.regionAvailable = value);
        public static Proxy<bool> PackagesSupportedProxy = new(() => instance.packagesSupported, value => instance.packagesSupported = value);
    }
}
