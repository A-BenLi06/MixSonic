namespace Subsonic8.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Services;
    using Client.Common.Services.DataStructures.SubsonicService;
    using MugenInjection.Attributes;
    using Subsonic8.Framework.Interfaces;
    using Subsonic8.Framework.Services;
    using Subsonic8.Main;
    using Subsonic8.Navidrome;
    using Windows.Security.Credentials;
    using Windows.UI.Xaml.Controls;

    public class SettingsViewModel : Screen
    {
        private readonly ICustomFrameAdapter _navigationService;
        private readonly IToastNotificationService _notificationService;
        private readonly IStorageService _storageService;
        private readonly ISubsonicService _subsonicService;
        private Subsonic8Configuration _configuration;
        private NamedOption<string> _selectedFormat;
        private string _bitRateText;
        private bool _isWorking;
        private string _error;
        private bool _saved;
        public SettingsViewModel(ISubsonicService subsonicService, IToastNotificationService notificationService,
            IStorageService storageService, ICustomFrameAdapter navigationService)
        {
            _subsonicService = subsonicService; _notificationService = notificationService;
            _storageService = storageService; _navigationService = navigationService;
            DisplayName = NavidromeText.Get("Settings");
            StreamFormats = new List<NamedOption<string>> {
                new NamedOption<string> { Value = "", Name = NavidromeText.Get("FormatDefault") },
                new NamedOption<string> { Value = "raw", Name = NavidromeText.Get("FormatOriginal") },
                new NamedOption<string> { Value = "mp3", Name = NavidromeText.Get("FormatMp3") }
            };
        }
        [Inject] public ISettingsHelper SettingsHelper { get; set; }
        [Inject] public INetworkDetectionService NetworkDetectionService { get; set; }
        public Subsonic8Configuration Configuration { get { return _configuration; } private set { _configuration = value; NotifyOfPropertyChange(); ValidateChanged(); } }
        public IList<NamedOption<string>> StreamFormats { get; private set; }
        public NamedOption<string> SelectedFormat { get { return _selectedFormat; } set { _selectedFormat = value; NotifyOfPropertyChange(); } }
        public string MaximumBitRateText { get { return _bitRateText; } set { _bitRateText = value; NotifyOfPropertyChange(); ValidateChanged(); } }
        public bool IsWorking { get { return _isWorking; } private set { _isWorking = value; NotifyOfPropertyChange(); ValidateChanged(); } }
        public string Error { get { return _error; } private set { _error = value; NotifyOfPropertyChange(); } }
        public bool CanApplyChanges { get { return !IsWorking && Configuration != null && ValidationMessage == null; } }
        public string ValidationMessage
        {
            get
            {
                if (Configuration == null) return null;
                var config = Configuration.SubsonicServiceConfiguration;
                if (string.IsNullOrWhiteSpace(config.PrimaryUrl) && string.IsNullOrWhiteSpace(config.SecondaryUrl)) return NavidromeText.Get("ServerAddressRequired");
                if (!ValidAddress(config.PrimaryUrl) || !ValidAddress(config.SecondaryUrl)) return NavidromeText.Get("InvalidServerAddress");
                if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrEmpty(config.Password)) return NavidromeText.Get("CredentialsRequired");
                int bitrate;
                if (!int.TryParse(MaximumBitRateText, NumberStyles.Integer, CultureInfo.CurrentCulture, out bitrate) || bitrate < 0) return NavidromeText.Get("InvalidBitRate");
                return null;
            }
        }
        private static bool ValidAddress(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            Uri address;
            var value = text.Trim();
            if (!value.Contains("://")) value = "http://" + value;
            return Uri.TryCreate(value, UriKind.Absolute, out address) &&
                (address.Scheme == "http" || address.Scheme == "https") && string.IsNullOrEmpty(address.UserInfo) && string.IsNullOrEmpty(address.Query) && string.IsNullOrEmpty(address.Fragment);
        }
        private void ValidateChanged() { NotifyOfPropertyChange(() => CanApplyChanges); NotifyOfPropertyChange(() => ValidationMessage); }
        public void PasswordChanged(PasswordBox input) { if (Configuration == null) return; Configuration.SubsonicServiceConfiguration.Password = input.Password; ValidateChanged(); }
        public void UsernameChanged(TextBox input) { if (Configuration == null) return; Configuration.SubsonicServiceConfiguration.Username = input.Text; ValidateChanged(); }
        public void PrimaryUrlChanged(TextBox input) { if (Configuration == null) return; Configuration.SubsonicServiceConfiguration.PrimaryUrl = input.Text; ValidateChanged(); }
        public void SecondaryUrlChanged(TextBox input) { if (Configuration == null) return; Configuration.SubsonicServiceConfiguration.SecondaryUrl = input.Text; ValidateChanged(); }
        public void BitRateChanged(TextBox input) { MaximumBitRateText = input.Text; }
        public async Task Populate()
        {
            IsWorking = true; Error = null;
            try
            {
                var configuration = await _storageService.Load<Subsonic8Configuration>() ?? new Subsonic8Configuration { UseToastNotifications = false };
                if (configuration.SubsonicServiceConfiguration == null) configuration.SubsonicServiceConfiguration = new SubsonicServiceConfiguration();
                var config = configuration.SubsonicServiceConfiguration;
                var credentials = SettingsHelper.GetCredentialsFromVault();
                if (credentials != null) { config.Username = credentials.UserName; config.Password = credentials.Password; }
                if (string.IsNullOrWhiteSpace(config.PrimaryUrl) && string.IsNullOrWhiteSpace(config.SecondaryUrl)) config.PrimaryUrl = config.BaseUrl;
                var format = config.CompatibleMode ? "mp3" : config.StreamingFormat ?? "";
                if (!StreamFormats.Any(option => option.Value == format)) StreamFormats.Add(new NamedOption<string> { Value = format, Name = format });
                SelectedFormat = StreamFormats.First(option => option.Value == format);
                MaximumBitRateText = (config.CompatibleMode && config.MaximumBitRate <= 0 ? 320 : Math.Max(0, config.MaximumBitRate)).ToString(CultureInfo.CurrentCulture);
                Configuration = configuration;
            }
            catch (Exception exception) { Error = exception.Message; }
            finally { IsWorking = false; }
        }
        public async Task ApplyChanges()
        {
            await SaveSettings();
            if (_saved && Error == null) _navigationService.NavigateToViewModel<MainViewModel>(true);
        }
        public async Task SaveSettings()
        {
            if (IsWorking) return;
            _saved = false;
            if (!CanApplyChanges) { Error = ValidationMessage; return; }
            IsWorking = true; Error = null;
            try
            {
                var config = Configuration.SubsonicServiceConfiguration;
                config.MaximumBitRate = int.Parse(MaximumBitRateText, CultureInfo.CurrentCulture);
                config.StreamingFormat = SelectedFormat == null ? null : SelectedFormat.Value;
                config.CompatibleMode = false;
                config.PrimaryUrl = string.IsNullOrWhiteSpace(config.PrimaryUrl) ? null : new SubsonicServiceConfiguration { BaseUrl = config.PrimaryUrl.Trim() }.BaseUrl;
                config.SecondaryUrl = string.IsNullOrWhiteSpace(config.SecondaryUrl) ? null : new SubsonicServiceConfiguration { BaseUrl = config.SecondaryUrl.Trim() }.BaseUrl;
                var result = await new RouteSelectionService(NetworkDetectionService).SelectBestRouteAsync(config.PrimaryUrl, config.SecondaryUrl, config.Username, config.Password);
                config.BaseUrl = result.Success ? result.SelectedUrl : config.PrimaryUrl ?? config.SecondaryUrl;
                await _storageService.Save(Configuration);
                SettingsHelper.UpdateCredentialsInVault(new PasswordCredential { UserName = config.Username, Password = config.Password, Resource = Framework.SettingsHelper.PasswordVaultResourceName });
                _subsonicService.Configuration = config;
                _notificationService.EnableNotifications = Configuration.UseToastNotifications;
                _saved = true;
                if (!result.Success) Error = string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("SettingsSavedUnreachable"), result.FailureReason);
            }
            catch (Exception exception) { Error = exception.Message; }
            finally { IsWorking = false; }
        }
        protected override async void OnActivate() { base.OnActivate(); await Populate(); }
    }
}
