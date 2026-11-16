namespace Mixdrome.Phone.Services
{
    using System;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Client.Common.Services;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Mixdrome.Phone.Common;
    using Windows.Security.Credentials;
    using Windows.Storage;

    public static class PhoneSettings
    {
        public const string CredentialResource = "Mixdrome.Phone";
        private static readonly RouteSelectionService Routes = new RouteSelectionService(new NetworkDetectionService());

        public static SubsonicServiceConfiguration Load()
        {
            var settings = ApplicationData.Current.LocalSettings;
            var configuration = new SubsonicServiceConfiguration
            {
                PrimaryUrl = ReadString(settings, "PrimaryUrl"),
                SecondaryUrl = ReadString(settings, "SecondaryUrl"),
                Username = ReadString(settings, "Username"),
                CompatibleMode = ReadBool(settings, "CompatibleMode"),
                StreamingFormat = ReadString(settings, "StreamingFormat"),
                MaximumBitRate = ReadInt(settings, "MaximumBitRate")
            };
            SubsonicAuthenticationMode authentication;
            if (Enum.TryParse(ReadString(settings, "AuthenticationMode"), out authentication) &&
                Enum.IsDefined(typeof(SubsonicAuthenticationMode), authentication))
                configuration.AuthenticationMode = authentication;

            if (!HasServer(configuration)) configuration.PrimaryUrl = ReadString(settings, "BaseUrl");
            configuration.BaseUrl = !string.IsNullOrWhiteSpace(configuration.PrimaryUrl)
                ? configuration.PrimaryUrl : configuration.SecondaryUrl;

            var vault = new PasswordVault();
            var credential = vault.RetrieveAll().FirstOrDefault(item =>
                item.Resource == CredentialResource && item.UserName == configuration.Username);
            if (credential != null)
            {
                credential.RetrievePassword();
                configuration.Password = credential.Password;
                // Remove the legacy plaintext value only after a vault credential exists.
                settings.Values.Remove("Password");
            }
            else
            {
                var legacyPassword = ReadString(settings, "Password");
                if (!string.IsNullOrWhiteSpace(configuration.Username) && !string.IsNullOrEmpty(legacyPassword))
                {
                    vault.Add(new PasswordCredential(CredentialResource, configuration.Username, legacyPassword));
                    configuration.Password = legacyPassword;
                    settings.Values.Remove("Password");
                }
            }
            return configuration;
        }

        public static bool HasServer(SubsonicServiceConfiguration configuration)
        {
            return !string.IsNullOrWhiteSpace(configuration.PrimaryUrl) ||
                !string.IsNullOrWhiteSpace(configuration.SecondaryUrl);
        }

        public static void Validate(SubsonicServiceConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");
            if (!HasServer(configuration)) throw new ArgumentException(PhoneText.Get("ServerAddressRequired"));
            configuration.PrimaryUrl = NormalizeAddress(configuration.PrimaryUrl);
            configuration.SecondaryUrl = NormalizeAddress(configuration.SecondaryUrl);
            configuration.Username = (configuration.Username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(configuration.Username) || string.IsNullOrEmpty(configuration.Password))
                throw new ArgumentException(PhoneText.Get("CredentialsRequired"));
            if (configuration.MaximumBitRate < 0) throw new ArgumentException(PhoneText.Get("InvalidBitRate"));
            if (!Enum.IsDefined(typeof(SubsonicAuthenticationMode), configuration.AuthenticationMode))
                throw new ArgumentException(PhoneText.Get("InvalidAuthentication"));
        }

        private static string NormalizeAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            if (!value.Contains("://")) value = "http://" + value;
            Uri address;
            if (!Uri.TryCreate(value, UriKind.Absolute, out address) ||
                (address.Scheme != "http" && address.Scheme != "https") ||
                !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) ||
                !string.IsNullOrEmpty(address.Fragment))
                throw new ArgumentException(PhoneText.Get("InvalidServerAddress"));
            return new SubsonicServiceConfiguration { BaseUrl = address.AbsoluteUri }.BaseUrl;
        }

        public static Task<RouteSelectionResult> SelectRouteAsync(SubsonicServiceConfiguration configuration,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Validate(configuration);
            return Routes.SelectBestRouteAsync(configuration, cancellationToken);
        }

        public static void Save(SubsonicServiceConfiguration configuration)
        {
            Validate(configuration);
            var vault = new PasswordVault();
            var previous = vault.RetrieveAll().Where(item => item.Resource == CredentialResource).ToList();
            var sameUser = previous.FirstOrDefault(item => item.UserName == configuration.Username);
            if (sameUser != null)
            {
                sameUser.RetrievePassword();
                vault.Remove(sameUser);
            }
            try { vault.Add(new PasswordCredential(CredentialResource, configuration.Username, configuration.Password)); }
            catch
            {
                if (sameUser != null) vault.Add(sameUser);
                throw;
            }

            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["PrimaryUrl"] = configuration.PrimaryUrl ?? string.Empty;
            settings.Values["SecondaryUrl"] = configuration.SecondaryUrl ?? string.Empty;
            settings.Values["BaseUrl"] = configuration.BaseUrl ?? string.Empty;
            settings.Values["Username"] = configuration.Username;
            settings.Values["AuthenticationMode"] = configuration.AuthenticationMode.ToString();
            settings.Values["StreamingFormat"] = configuration.StreamingFormat ?? string.Empty;
            settings.Values["MaximumBitRate"] = configuration.MaximumBitRate;
            settings.Values["CompatibleMode"] = configuration.CompatibleMode;
            settings.Values.Remove("Password");
            foreach (var item in previous.Where(item => item != sameUser)) vault.Remove(item);
        }

        private static string ReadString(ApplicationDataContainer settings, string key)
        {
            object value;
            return settings.Values.TryGetValue(key, out value) ? value as string ?? string.Empty : string.Empty;
        }

        private static bool ReadBool(ApplicationDataContainer settings, string key)
        {
            object value;
            return settings.Values.TryGetValue(key, out value) && value is bool && (bool)value;
        }

        private static int ReadInt(ApplicationDataContainer settings, string key)
        {
            object value;
            int result;
            return settings.Values.TryGetValue(key, out value) &&
                int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out result) ? result : 0;
        }
    }
}
