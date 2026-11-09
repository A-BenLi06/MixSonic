namespace Subsonic8.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models.Navidrome;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Client.Common.Services.Navidrome;
    using Windows.Security.Cryptography;
    using Windows.Security.Cryptography.Core;
    using Windows.Storage;

    public interface INavidromeSessionService : INotifyPropertyChanged
    {
        event EventHandler LibraryChanged;
        ServerCapabilities Capabilities { get; }
        ServerUser User { get; }
        BindableCollection<MusicFolder> Folders { get; }
        MusicFolder SelectedFolder { get; set; }
        string FolderId { get; }
        string AccountKey { get; }
        string Status { get; }
        string CapabilityError { get; }
        string UserError { get; }
        bool IsConnected { get; }
        Task ConnectAsync(bool refresh = false);
        NavidromeApi CreateApi();
    }

    public sealed class NavidromeSessionService : PropertyChangedBase, INavidromeSessionService
    {
        private readonly ISubsonicService _subsonic;
        private Task _connecting;
        private string _connectedAccount;
        private string _connectedPassword;
        private MusicFolder _selectedFolder;
        private int _version;
        private bool _ready;
        private SubsonicServiceConfiguration _configuration;

        public NavidromeSessionService(ISubsonicService subsonic)
        {
            _subsonic = subsonic;
            Folders = new BindableCollection<MusicFolder>();
            var notifyingService = subsonic as INotifyPropertyChanged;
            if (notifyingService != null) notifyingService.PropertyChanged += ServiceChanged;
            ObserveConfiguration();
        }

        public event EventHandler LibraryChanged;
        public ServerCapabilities Capabilities { get; private set; }
        public ServerUser User { get; private set; }
        public BindableCollection<MusicFolder> Folders { get; private set; }
        public string AccountKey { get { return AccountIdentity.For(_subsonic.Configuration); } }
        public string FolderId { get { return SelectedFolder == null ? null : SelectedFolder.Id; } }
        public string Status { get; private set; }
        public string CapabilityError { get; private set; }
        public string UserError { get; private set; }
        public bool IsConnected { get { return _ready && _connectedAccount == AccountKey && _subsonic.Configuration != null && _connectedPassword == _subsonic.Configuration.Password; } }
        public MusicFolder SelectedFolder
        {
            get { return _selectedFolder; }
            set
            {
                if (value == _selectedFolder || (value != null && !Folders.Contains(value))) return;
                _selectedFolder = value;
                if (!string.IsNullOrEmpty(AccountKey)) ApplicationData.Current.LocalSettings.Values["library-" + Hash(AccountKey)] = value == null ? string.Empty : value.Id ?? string.Empty;
                NotifyOfPropertyChange(() => SelectedFolder);
                NotifyOfPropertyChange(() => FolderId);
                var handler = LibraryChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public static string Hash(string value)
        {
            var provider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256);
            return CryptographicBuffer.EncodeToHexString(provider.HashData(CryptographicBuffer.ConvertStringToBinary(value, BinaryStringEncoding.Utf8)));
        }

        public Task ConnectAsync(bool refresh = false)
        {
            var account = AccountKey;
            var password = _subsonic.Configuration == null ? null : _subsonic.Configuration.Password;
            if (string.IsNullOrEmpty(account)) throw new InvalidOperationException(NavidromeText.Get("ConfigureServer"));
            if (!refresh && _connectedAccount == account && _connectedPassword == password && _connecting != null) return _connecting;
            _connectedAccount = account;
            _connectedPassword = password;
            _ready = false; Capabilities = null; User = null; CapabilityError = null; UserError = null; _selectedFolder = null; Folders.Clear();
            _connecting = ConnectCore(new NavidromeApi(_subsonic.Configuration), ++_version);
            return _connecting;
        }

        private async Task ConnectCore(NavidromeApi api, int version)
        {
            try
            {
                var ping = api.Ping();
                await ping.ReadAsync();
                if (version != _version || api.AccountKey != AccountKey || _connectedPassword != _subsonic.Configuration.Password) return;
                if (_subsonic.Configuration.AuthenticationMode == SubsonicAuthenticationMode.ApiKey)
                {
                    var username = await api.GetTokenInfo().ReadAsync();
                    if (!string.Equals(username, _subsonic.Configuration.Username, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(NavidromeText.Get("ApiKeyAccountMismatch"));
                }
                var extensions = api.GetExtensions();
                var folders = api.GetMusicFolders();
                var user = api.GetUser();
                await Task.WhenAll(extensions.Execute(), folders.Execute(), user.Execute());
                if (folders.Error != null) throw folders.Error;
                if (version != _version || api.AccountKey != AccountKey || _connectedPassword != _subsonic.Configuration.Password) return;
                Capabilities = ping.Server;
                if (extensions.Error == null) foreach (var extension in extensions.Result) Capabilities.Extensions[extension.Key] = extension.Value;
                User = user.Error == null ? user.Result : null;
                CapabilityError = extensions.Error == null ? null : extensions.Error.Message;
                UserError = user.Error == null ? null : user.Error.Message;
                Folders.Add(new MusicFolder { Id = null, Name = NavidromeText.Get("AllLibraries") });
                Folders.AddRange(folders.Result);
                object savedValue;
                ApplicationData.Current.LocalSettings.Values.TryGetValue("library-" + Hash(AccountKey), out savedValue);
                var saved = savedValue as string;
                _selectedFolder = Folders.FirstOrDefault(folder => folder.Id == saved) ?? Folders.First();
                Status = string.Join(" ", new[] { Capabilities.Type, Capabilities.ServerVersion }.Where(s => !string.IsNullOrEmpty(s)));
                _ready = true;
                NotifyOfPropertyChange(() => Capabilities); NotifyOfPropertyChange(() => User);
                NotifyOfPropertyChange(() => CapabilityError); NotifyOfPropertyChange(() => UserError);
                NotifyOfPropertyChange(() => SelectedFolder); NotifyOfPropertyChange(() => FolderId); NotifyOfPropertyChange(() => Status);
                NotifyOfPropertyChange(() => IsConnected);
            }
            catch
            {
                if (version == _version) { _ready = false; _connecting = null; _connectedAccount = null; NotifyOfPropertyChange(() => IsConnected); }
                throw;
            }
        }

        public NavidromeApi CreateApi()
        {
            var api = new NavidromeApi(_subsonic.Configuration);
            api.UseFormPost = api.AccountKey == _connectedAccount && Capabilities != null && Capabilities.Supports("formPost");
            return api;
        }

        private void ObserveConfiguration()
        {
            if (_configuration != null) _configuration.PropertyChanged -= ConfigurationChanged;
            _configuration = _subsonic.Configuration;
            if (_configuration != null) _configuration.PropertyChanged += ConfigurationChanged;
        }
        private void ServiceChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != "Configuration") return;
            ObserveConfiguration(); ResetConnection();
        }
        private void ConfigurationChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == "BaseUrl" || args.PropertyName == "Username" || args.PropertyName == "Password" || args.PropertyName == "AuthenticationMode") ResetConnection();
        }
        private void ResetConnection()
        {
            ++_version; _ready = false; _connecting = null; _connectedAccount = _connectedPassword = null;
            Capabilities = null; User = null; Status = null; CapabilityError = null; UserError = null; _selectedFolder = null; Folders.Clear();
            NotifyOfPropertyChange(() => AccountKey); NotifyOfPropertyChange(() => IsConnected);
            NotifyOfPropertyChange(() => User); NotifyOfPropertyChange(() => Capabilities); NotifyOfPropertyChange(() => Status);
            NotifyOfPropertyChange(() => CapabilityError); NotifyOfPropertyChange(() => UserError);
            NotifyOfPropertyChange(() => SelectedFolder); NotifyOfPropertyChange(() => FolderId);
            var handler = LibraryChanged; if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
