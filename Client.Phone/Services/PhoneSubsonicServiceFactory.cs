namespace Mixdrome.Phone.Services
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Client.Common.Services;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Mixdrome.Phone.Common;

    public static class PhoneSubsonicServiceFactory
    {
        public static async Task<ISubsonicService> CreateAsync()
        {
            return new SubsonicService { Configuration = await LoadConfigurationAsync() };
        }

        public static async Task<WP8SubsonicService> CreatePlaybackServiceAsync()
        {
            var service = new WP8SubsonicService();
            service.Configure(await LoadConfigurationAsync());
            return service;
        }

        public static async Task<SubsonicServiceConfiguration> LoadConfigurationAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var configuration = PhoneSettings.Load();
            if (!PhoneSettings.HasServer(configuration)) return configuration;

            PhoneSettings.Validate(configuration);
            var route = await PhoneSettings.SelectRouteAsync(configuration, cancellationToken);
            if (!route.Success)
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
                    PhoneText.Get("ConnectionFailed"), route.FailureReason));
            configuration.BaseUrl = route.SelectedUrl;
            return configuration;
        }
    }
}
