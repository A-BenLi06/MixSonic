namespace Mixdrome.Phone.Services
{
    using System;
    using System.Threading.Tasks;
    using Client.Common.Services;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Windows.Storage;

    public static class PhoneSubsonicServiceFactory
    {
        public static async Task<ISubsonicService> CreateAsync()
        {
            var service = new SubsonicService();
            service.Configuration = await LoadConfiguration();
            return service;
        }

        public static async Task<WP8SubsonicService> CreatePlaybackServiceAsync()
        {
            var configuration = await LoadConfiguration();
            var service = new WP8SubsonicService();
            service.Configure(configuration.BaseUrl, configuration.Username, configuration.Password);
            service.CompatibleMode = configuration.CompatibleMode;
            return service;
        }

        private static async Task<SubsonicServiceConfiguration> LoadConfiguration()
        {
            var config = new SubsonicServiceConfiguration();

            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                config.Username = GetString(settings, "Username");
                config.Password = GetString(settings, "Password");
                config.CompatibleMode = settings.Values.ContainsKey("CompatibleMode")
                                        && (bool)settings.Values["CompatibleMode"];

                var primaryUrl = GetString(settings, "PrimaryUrl");
                var secondaryUrl = GetString(settings, "SecondaryUrl");

                if (string.IsNullOrEmpty(primaryUrl) && settings.Values.ContainsKey("BaseUrl"))
                {
                    primaryUrl = GetString(settings, "BaseUrl");
                }

                config.PrimaryUrl = primaryUrl;
                config.SecondaryUrl = secondaryUrl;

                if (!string.IsNullOrEmpty(primaryUrl) || !string.IsNullOrEmpty(secondaryUrl))
                {
                    var networkService = new NetworkDetectionService();
                    var routeService = new RouteSelectionService(networkService);
                    var result = await routeService.SelectBestRouteAsync(
                        primaryUrl,
                        secondaryUrl,
                        config.Username,
                        config.Password);

                    config.BaseUrl = result.Success
                        ? result.SelectedUrl
                        : (!string.IsNullOrEmpty(secondaryUrl) ? secondaryUrl : primaryUrl);
                }
            }
            catch
            {
                try
                {
                    var settings = ApplicationData.Current.LocalSettings;
                    var primaryUrl = GetString(settings, "PrimaryUrl");
                    if (string.IsNullOrEmpty(primaryUrl) && settings.Values.ContainsKey("BaseUrl"))
                    {
                        primaryUrl = GetString(settings, "BaseUrl");
                    }

                    config.BaseUrl = primaryUrl;
                    config.PrimaryUrl = primaryUrl;
                    config.SecondaryUrl = GetString(settings, "SecondaryUrl");
                    config.Username = GetString(settings, "Username");
                    config.Password = GetString(settings, "Password");
                }
                catch
                {
                }
            }

            return config;
        }

        private static string GetString(ApplicationDataContainer settings, string key)
        {
            return settings.Values.ContainsKey(key) ? settings.Values[key] as string ?? string.Empty : string.Empty;
        }
    }
}
