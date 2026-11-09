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
        Task ConnectAsync();
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

        public NavidromeSessionService(ISubsonicService subsonic)
        {
            _subsonic = subsonic;
            Folders = new BindableCollection<MusicFolder>();
        }

        public event EventHandler LibraryChanged;
        public ServerCapabilities Capabilities { get; private set; }
        public ServerUser User { get; private set; }
        public BindableCollection<MusicFolder> Folders { get; private set; }
        public string AccountKey { get { return AccountIdentity.For(_subsonic.Configuration); } }
        public string FolderId { get { return SelectedFolder == null ? null : SelectedFolder.Id; } }
        public string Status { get; private set; }
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

        public Task ConnectAsync()
        {
            var account = AccountKey;
            var password = _subsonic.Configuration == null ? null : _subsonic.Configuration.Password;
            if (string.IsNullOrEmpty(account)) throw new InvalidOperationException(NavidromeText.Get("ConfigureServer"));
            if (_connectedAccount == account && _connectedPassword == password && _connecting != null) return _connecting;
            _connectedAccount = account;
            _connectedPassword = password;
            Capabilities = null; User = null; _selectedFolder = null; Folders.Clear();
            _connecting = ConnectCore(new NavidromeApi(_subsonic.Configuration), ++_version);
            return _connecting;
        }

        private async Task ConnectCore(NavidromeApi api, int version)
        {
            try
            {
                var ping = api.Ping();
                await ping.ReadAsync();
                var extensions = api.GetExtensions();
                var folders = api.GetMusicFolders();
                var user = api.GetUser();
                await Task.WhenAll(extensions.Execute(), folders.Execute(), user.Execute());
                if (folders.Error != null) throw folders.Error;
                if (version != _version || api.AccountKey != AccountKey || _connectedPassword != _subsonic.Configuration.Password) return;
                Capabilities = ping.Server;
                if (extensions.Error == null) foreach (var extension in extensions.Result) Capabilities.Extensions[extension.Key] = extension.Value;
                User = user.Error == null ? user.Result : null;
                Folders.Add(new MusicFolder { Id = null, Name = NavidromeText.Get("AllLibraries") });
                Folders.AddRange(folders.Result);
                var saved = ApplicationData.Current.LocalSettings.Values["library-" + Hash(AccountKey)] as string;
                _selectedFolder = Folders.FirstOrDefault(folder => folder.Id == saved) ?? Folders.First();
                Status = string.Join(" ", new[] { Capabilities.Type, Capabilities.ServerVersion }.Where(s => !string.IsNullOrEmpty(s)));
                NotifyOfPropertyChange(() => Capabilities); NotifyOfPropertyChange(() => User);
                NotifyOfPropertyChange(() => SelectedFolder); NotifyOfPropertyChange(() => FolderId); NotifyOfPropertyChange(() => Status);
            }
            catch
            {
                if (version == _version) { _connecting = null; _connectedAccount = null; }
                throw;
            }
        }

        public NavidromeApi CreateApi()
        {
            var api = new NavidromeApi(_subsonic.Configuration);
            api.UseFormPost = api.AccountKey == _connectedAccount && Capabilities != null && Capabilities.Supports("formPost");
            return api;
        }
    }
}
