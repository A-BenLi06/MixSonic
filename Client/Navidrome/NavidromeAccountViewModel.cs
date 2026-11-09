namespace Subsonic8.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models.Navidrome;
    using MugenInjection.Attributes;
    using Subsonic8.Framework.Services;
    using Subsonic8.Settings;
    using Windows.UI.Xaml;

    public interface INavidromeAccountViewModel : IScreen { }
    public sealed class NavidromeAccountViewModel : NavidromePageViewModel, INavidromeAccountViewModel
    {
        private readonly DispatcherTimer _timer;
        private ScanStatus _scan;
        private bool _polling;
        public NavidromeAccountViewModel()
        {
            UpdateDisplayName = () => { };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _timer.Tick += async (sender, args) => { if (IsScanning && !IsWorking && !_polling) await PollScan(); };
        }
        [Inject] public IDialogService DialogService { get; set; }
        public string Avatar { get { return Session.IsConnected && Session.User != null ? Session.CreateApi().AvatarUrl() : null; } }
        public bool CanStartScan { get { return !IsLoading && !IsWorking && !IsScanning && Session.IsConnected && Session.User != null && Session.User.AdminRole; } }
        public bool CanJukebox { get { return !IsLoading && !IsWorking && Session.User != null && Session.User.JukeboxRole; } }
        public bool IsScanning { get { return _scan != null && _scan.Scanning; } }
        public string ScanMessage { get { return _scan == null ? NavidromeText.Get("ScanUnknown") : string.Format(CultureInfo.CurrentCulture, NavidromeText.Get(_scan.Scanning ? "ScanInProgress" : "ScanIdle"), _scan.Count); } }
        public string LastScan
        {
            get
            {
                if (_scan == null || string.IsNullOrEmpty(_scan.LastScan)) return null;
                DateTimeOffset time;
                var text = DateTimeOffset.TryParse(_scan.LastScan, CultureInfo.InvariantCulture, DateTimeStyles.None, out time) ? time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : _scan.LastScan;
                return string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("LastScan"), text);
            }
        }
        public string Roles
        {
            get
            {
                if (Session.User == null) return NavidromeText.Get("RolesUnknown");
                var roles = new List<string>();
                if (Session.User.AdminRole) roles.Add(NavidromeText.Get("RoleAdministrator"));
                if (Session.User.StreamRole) roles.Add(NavidromeText.Get("RoleStreaming"));
                if (Session.User.DownloadRole) roles.Add(NavidromeText.Get("RoleDownloads"));
                if (Session.User.ShareRole) roles.Add(NavidromeText.Get("RoleSharing"));
                if (Session.User.JukeboxRole) roles.Add(NavidromeText.Get("RoleJukebox"));
                return roles.Count == 0 ? NavidromeText.Get("NoRoles") : string.Join(", ", roles);
            }
        }
        public string Extensions
        {
            get { return Session.Capabilities == null ? null : string.Join(Environment.NewLine, Session.Capabilities.Extensions.OrderBy(pair => pair.Key).Select(pair => pair.Key + " (" + string.Join(", ", pair.Value) + ")")); }
        }
        protected override void OnActivate() { Session.PropertyChanged += SessionChanged; _timer.Start(); base.OnActivate(); }
        protected override void OnDeactivate(bool close) { _timer.Stop(); Session.PropertyChanged -= SessionChanged; base.OnDeactivate(close); }
        private void SessionChanged(object sender, PropertyChangedEventArgs args) { Changed(); }
        protected override void Changed()
        {
            base.Changed();
            foreach (var name in new[] { "Avatar", "CanStartScan", "CanJukebox", "IsScanning", "ScanMessage", "LastScan", "Roles", "Extensions" }) NotifyOfPropertyChange(name);
        }
        public override async Task Reload()
        {
            var version = ++LoadVersion; IsLoading = true; Error = null; _scan = null; DisplayName = NavidromeText.Get("Account");
            try
            {
                await Session.ConnectAsync(true); var api = Session.CreateApi();
                var scan = await api.GetScanStatus().ReadAsync();
                if (IsCurrent(version, api)) _scan = scan;
            }
            catch (Exception exception) { if (version == LoadVersion) Error = exception.Message; }
            finally { if (version == LoadVersion) { IsLoading = false; Changed(); } }
        }
        private async Task PollScan()
        {
            _polling = true; var version = LoadVersion;
            try
            {
                var api = Session.CreateApi(); var scan = await api.GetScanStatus().ReadAsync();
                if (IsCurrent(version, api)) { _scan = scan; Error = null; Changed(); }
            }
            catch (Exception exception) { if (PageActive && version == LoadVersion) Error = exception.Message; }
            finally { _polling = false; }
        }
        public Task StartScan()
        {
            var version = LoadVersion;
            return RunOperation(async api =>
            {
                if (Session.User == null || !Session.User.AdminRole) throw new InvalidOperationException(NavidromeText.Get("AdministratorRequired"));
                var scan = await api.GetScanStatus(true).ReadAsync();
                if (IsCurrent(version, api)) { _scan = scan; Changed(); }
            });
        }
        public void Settings() { DialogService.ShowSettings<SettingsViewModel>(); }
        public void Jukebox() { NavigationService.NavigateToViewModel<NavidromeJukeboxViewModel>(); }
    }
}
