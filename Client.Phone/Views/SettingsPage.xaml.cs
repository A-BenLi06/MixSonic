namespace Mixdrome.Phone.Views
{
    using System;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Mixdrome.Phone.Common;
    using Mixdrome.Phone.Services;
    using Windows.Phone.UI.Input;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Navigation;

    public sealed partial class SettingsPage : Page
    {
        private bool _pageActive;
        private bool _isWorking;
        private CancellationTokenSource _connectionCancellation;

        public SettingsPage()
        {
            InitializeComponent();
            AuthenticationComboBox.ItemsSource = Enum.GetValues(typeof(SubsonicAuthenticationMode))
                .Cast<SubsonicAuthenticationMode>().Select(mode => new AuthenticationOption
                { Value = mode, Name = PhoneText.Get("Authentication" + mode) }).ToList();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pageActive = true;
            HardwareButtons.BackPressed += OnHardwareBackPressed;
            try
            {
                var configuration = PhoneSettings.Load();
                PrimaryUrlTextBox.Text = configuration.PrimaryUrl ?? string.Empty;
                SecondaryUrlTextBox.Text = configuration.SecondaryUrl ?? string.Empty;
                UsernameTextBox.Text = configuration.Username ?? string.Empty;
                PasswordBox.Password = configuration.Password ?? string.Empty;
                StreamingFormatTextBox.Text = configuration.StreamingFormat ?? string.Empty;
                MaximumBitRateTextBox.Text = configuration.MaximumBitRate.ToString(CultureInfo.CurrentCulture);
                CompatibleModeToggle.IsOn = configuration.CompatibleMode;
                AuthenticationComboBox.SelectedItem = AuthenticationComboBox.Items.Cast<AuthenticationOption>()
                    .First(option => option.Value == configuration.AuthenticationMode);
                StatusText.Text = string.Empty;
            }
            catch (Exception exception) { StatusText.Text = exception.Message; }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _pageActive = false;
            HardwareButtons.BackPressed -= OnHardwareBackPressed;
            if (_connectionCancellation != null) _connectionCancellation.Cancel();
            base.OnNavigatedFrom(e);
        }

        private void OnHardwareBackPressed(object sender, BackPressedEventArgs e)
        {
            if (e.Handled || !Frame.CanGoBack) return;
            e.Handled = true;
            Frame.GoBack();
        }

        private void OnAuthenticationChanged(object sender, SelectionChangedEventArgs e)
        {
            var option = AuthenticationComboBox.SelectedItem as AuthenticationOption;
            if (option == null || CredentialLabel == null || PasswordBox == null) return;
            CredentialLabel.Text = option.Name;
            PasswordBox.PlaceholderText = option.Name;
            CredentialHelp.Text = PhoneText.Get(option.Value == SubsonicAuthenticationMode.ApiKey ? "ApiKeyHelp" : "PasswordHelp");
        }

        private SubsonicServiceConfiguration ReadConfiguration()
        {
            int bitrate;
            if (!int.TryParse(MaximumBitRateTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out bitrate) || bitrate < 0)
                throw new ArgumentException(PhoneText.Get("InvalidBitRate"));
            var option = AuthenticationComboBox.SelectedItem as AuthenticationOption;
            var configuration = new SubsonicServiceConfiguration
            {
                PrimaryUrl = PrimaryUrlTextBox.Text, SecondaryUrl = SecondaryUrlTextBox.Text,
                Username = UsernameTextBox.Text, Password = PasswordBox.Password,
                AuthenticationMode = option == null ? SubsonicAuthenticationMode.Password : option.Value,
                StreamingFormat = StreamingFormatTextBox.Text.Trim(), MaximumBitRate = bitrate,
                CompatibleMode = CompatibleModeToggle.IsOn
            };
            PhoneSettings.Validate(configuration);
            return configuration;
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e) { await ConnectAsync(true); }
        private async void OnTestConnectionClick(object sender, RoutedEventArgs e) { await ConnectAsync(false); }

        private async Task ConnectAsync(bool save)
        {
            if (_isWorking) return;
            _isWorking = true;
            SetEditing(false);
            SaveButton.IsEnabled = TestButton.IsEnabled = false;
            ConnectionProgress.IsActive = true;
            _connectionCancellation = new CancellationTokenSource();
            try
            {
                var configuration = ReadConfiguration();
                StatusText.Text = PhoneText.Get("TestingConnection");
                var result = await PhoneSettings.SelectRouteAsync(configuration, _connectionCancellation.Token);
                _connectionCancellation.Token.ThrowIfCancellationRequested();
                if (!_pageActive) return;
                configuration.BaseUrl = result.Success ? result.SelectedUrl : configuration.PrimaryUrl ?? configuration.SecondaryUrl;
                if (save)
                {
                    PhoneSettings.Save(configuration);
                    StatusText.Text = result.Success ? PhoneText.Get("SettingsSaved") :
                        string.Format(CultureInfo.CurrentCulture, PhoneText.Get("SettingsSavedUnreachable"), result.FailureReason);
                    if (result.Success && Frame.CanGoBack) Frame.GoBack();
                }
                else StatusText.Text = string.Format(CultureInfo.CurrentCulture,
                    PhoneText.Get(result.Success ? "ConnectionSuccessful" : "ConnectionFailed"),
                    result.Success ? result.SelectedUrl : result.FailureReason);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (_pageActive) StatusText.Text = exception.Message; }
            finally
            {
                _connectionCancellation.Dispose();
                _connectionCancellation = null;
                _isWorking = false;
                SetEditing(true);
                SaveButton.IsEnabled = TestButton.IsEnabled = true;
                ConnectionProgress.IsActive = false;
            }
        }

        private void SetEditing(bool enabled)
        {
            foreach (var control in new Control[] { PrimaryUrlTextBox, SecondaryUrlTextBox, UsernameTextBox,
                AuthenticationComboBox, PasswordBox, StreamingFormatTextBox, MaximumBitRateTextBox, CompatibleModeToggle })
                control.IsEnabled = enabled;
        }

        public sealed class AuthenticationOption
        {
            public SubsonicAuthenticationMode Value { get; set; }
            public string Name { get; set; }
        }
    }
}
