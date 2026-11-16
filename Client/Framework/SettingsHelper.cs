namespace Mixdrome.Framework
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Client.Common;
    using Client.Common.Services;
    using MugenInjection.Attributes;
    using Mixdrome.Framework.Interfaces;
    using Mixdrome.Framework.Services;
    using Mixdrome.Settings;
    using Windows.Security.Credentials;

    public class SettingsHelper : ISettingsHelper
    {
        #region Constants

        public const string PasswordVaultResourceName = "Mixdrome";

        #endregion

        #region Public Properties

        [Inject]
        public IStorageService StorageService { get; set; }

        [Inject]
        public ISubsonicService SubsonicService { get; set; }

        [Inject]
        public IToastNotificationService ToastNotificationService { get; set; }

        #endregion

        #region Public Methods and Operators

        public PasswordCredential GetCredentialsFromVault()
        {
            var vault = new PasswordVault();
            return GetCredentialsFromVault(vault);
        }

        public async Task LoadSettings()
        {
            var MixdromeConfiguration = await GetMixdromeConfiguration();
            var svcConfig = MixdromeConfiguration.SubsonicServiceConfiguration;

            // Perform route selection to find the best working URL
            if (!string.IsNullOrEmpty(svcConfig.PrimaryUrl) || !string.IsNullOrEmpty(svcConfig.SecondaryUrl))
            {
                var networkService = new NetworkDetectionService();
                var routeService = new RouteSelectionService(networkService);

                var result = await routeService.SelectBestRouteAsync(svcConfig);

                if (result.Success)
                {
                    svcConfig.BaseUrl = result.SelectedUrl;
                }
                else
                {
                    // Fallback: prefer SecondaryUrl (external/DDNS) over PrimaryUrl (internal)
                    // This is safer for mobile networks where internal IPs won't work
                    svcConfig.BaseUrl = !string.IsNullOrEmpty(svcConfig.SecondaryUrl)
                        ? svcConfig.SecondaryUrl
                        : svcConfig.PrimaryUrl;
                }
            }

            SubsonicService.Configuration = svcConfig;
            ToastNotificationService.EnableNotifications = MixdromeConfiguration.UseToastNotifications;
        }

        public void UpdateCredentialsInVault(PasswordCredential passwordCredential)
        {
            var vault = new PasswordVault();
            var credentials = GetCredentialsFromVault(vault);
            if (credentials != null)
            {
                vault.Remove(credentials);
            }

            vault.Add(passwordCredential);
        }

        #endregion

        #region Methods

        private PasswordCredential GetCredentialsFromVault(PasswordVault vault)
        {
            PasswordCredential credentials;
            try
            {
                credentials = vault.FindAllByResource(PasswordVaultResourceName).FirstOrDefault();
                credentials = vault.Retrieve(PasswordVaultResourceName, credentials.UserName);
            }
            catch (Exception exception)
            {
                credentials = null;
                this.Log(exception);
            }

            return credentials;
        }

        private async Task<Tuple<string, string>> GetLegacyCredentials()
        {
            Tuple<string, string> credentials = null;
            var data = await StorageService.GetData<MixdromeConfiguration>();
            if (data != null)
            {
                var xDocument = XDocument.Parse(data);
                var configurationElement =
                    xDocument.Element("MixdromeConfiguration").Element("SubsonicServiceConfiguration");
                var usernameElement = configurationElement.Element("Username");
                var passwordElement = configurationElement.Element("Password");
                if (usernameElement != null && passwordElement != null)
                {
                    credentials = new Tuple<string, string>(usernameElement.Value, passwordElement.Value);
                }
            }

            return credentials;
        }

        private async Task<MixdromeConfiguration> GetMixdromeConfiguration()
        {
            var MixdromeConfiguration = await StorageService.Load<MixdromeConfiguration>()
                                         ?? new MixdromeConfiguration();
            await SetPasswordFromVault(MixdromeConfiguration);

            return MixdromeConfiguration;
        }

        private async Task SetPasswordFromVault(MixdromeConfiguration appConfiguration)
        {
            var configuration = appConfiguration.SubsonicServiceConfiguration;
            var vault = new PasswordVault();
            var credentials = GetCredentialsFromVault(vault);
            if (credentials == null)
            {
                if (!string.IsNullOrWhiteSpace(configuration.BaseUrl))
                {
                    var legacyCredentials = await GetLegacyCredentials();
                    if (legacyCredentials != null)
                    {
                        var passwordCredential = new PasswordCredential(
                            PasswordVaultResourceName, legacyCredentials.Item1, legacyCredentials.Item2);
                        vault.Add(passwordCredential);
                        configuration.Username = legacyCredentials.Item1;
                        configuration.Password = legacyCredentials.Item2;
                        await StorageService.Save(appConfiguration);
                    }
                }
            }
            else
            {
                configuration.Username = credentials.UserName;
                configuration.Password = credentials.Password;
            }
        }

        #endregion
    }
}
