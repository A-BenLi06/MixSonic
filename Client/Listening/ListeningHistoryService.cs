namespace Subsonic8.Listening
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.EventAggregatorMessages;
    using Client.Common.Services;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Windows.Security.Cryptography;
    using Windows.Security.Cryptography.Core;
    using Windows.Storage;

    public class ListeningHistoryService : PropertyChangedBase, IListeningHistoryService, IHandle<TrackPlaybackStartedMessage>
    {
        private readonly ISubsonicService _subsonicService;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private ListeningHistory _history = new ListeningHistory();
        private string _account;
        private bool _readFailed;
        private SubsonicServiceConfiguration _configuration;

        public ListeningHistoryService(ISubsonicService subsonicService, IEventAggregator events)
        {
            _subsonicService = subsonicService;
            events.Subscribe(this);
            var notifyingService = subsonicService as INotifyPropertyChanged;
            if (notifyingService != null) notifyingService.PropertyChanged += OnServiceChanged;
            ObserveConfiguration();
        }

        public IEnumerable<RecentTrack> Entries
        {
            get { return _account == AccountIdentity.For(_subsonicService.Configuration)
                ? _history.Entries : Enumerable.Empty<RecentTrack>(); }
        }

        public string Error { get; private set; }

        public async Task RefreshAsync()
        {
            await _gate.WaitAsync();
            try
            {
                await LoadAccount(AccountIdentity.For(_subsonicService.Configuration));
            }
            catch (Exception)
            {
                Error = MixRadioText.Get("HistoryReadError");
                NotifyOfPropertyChange(() => Error);
            }
            finally { _gate.Release(); }
        }

        public async void Handle(TrackPlaybackStartedMessage message)
        {
            var item = message.Item;
            if (item == null || item.Type != Client.Common.Models.PlaylistItemTypeEnum.Audio ||
                string.IsNullOrWhiteSpace(item.SongId) || string.IsNullOrEmpty(item.AccountKey)) return;
            var account = item.AccountKey;
            await _gate.WaitAsync();
            try
            {
                if (account != AccountIdentity.For(_subsonicService.Configuration)) return;
                await LoadAccount(account);
                if (account != AccountIdentity.For(_subsonicService.Configuration)) return;
                _history.Record(new RecentTrack
                {
                    SongId = item.SongId, CoverArtId = item.CoverArtId, Title = item.Title,
                    Artist = item.Artist, Duration = item.Duration, PlayedAt = DateTimeOffset.Now
                });
                NotifyOfPropertyChange(() => Entries);
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FileName(account) + ".pending", CreationCollisionOption.ReplaceExisting);
                using (var stream = new MemoryStream())
                {
                    new DataContractSerializer(typeof(ListeningHistory)).WriteObject(stream, _history);
                    await FileIO.WriteTextAsync(file, Encoding.UTF8.GetString(stream.ToArray(), 0, (int)stream.Length));
                }
                await file.RenameAsync(FileName(account), NameCollisionOption.ReplaceExisting);
                _readFailed = false;
                Error = null;
                NotifyOfPropertyChange(() => Error);
            }
            catch (Exception)
            {
                Error = MixRadioText.Get("HistoryWriteError");
                NotifyOfPropertyChange(() => Error);
            }
            finally { _gate.Release(); }
        }

        private async Task LoadAccount(string account)
        {
            if (_account == account && !_readFailed) return;
            _history = new ListeningHistory();
            _account = null;
            _readFailed = false;
            Error = null;
            NotifyOfPropertyChange(() => Entries);
            NotifyOfPropertyChange(() => Error);
            if (string.IsNullOrEmpty(account)) return;
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(FileName(account));
                var text = await FileIO.ReadTextAsync(file);
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                {
                    var stored = (ListeningHistory)new DataContractSerializer(typeof(ListeningHistory)).ReadObject(stream);
                    if (stored.Entries != null)
                        _history.Entries = stored.Entries.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.SongId))
                            .OrderByDescending(entry => entry.PlayedAt).GroupBy(entry => entry.SongId)
                            .Select(group => group.First()).Take(ListeningHistory.MaximumEntries).ToList();
                }
            }
            catch (FileNotFoundException) { }
            catch (SerializationException)
            {
                _history = new ListeningHistory();
                _readFailed = true;
                Error = MixRadioText.Get("HistoryReadError");
                NotifyOfPropertyChange(() => Error);
            }
            if (account != AccountIdentity.For(_subsonicService.Configuration)) return;
            _account = account;
            NotifyOfPropertyChange(() => Entries);
        }

        private static string FileName(string account)
        {
            var algorithm = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256);
            var buffer = CryptographicBuffer.ConvertStringToBinary(account, BinaryStringEncoding.Utf8);
            return "listening-history-" + CryptographicBuffer.EncodeToHexString(algorithm.HashData(buffer)) + ".xml";
        }

        private void ObserveConfiguration()
        {
            if (_configuration != null) _configuration.PropertyChanged -= OnConfigurationChanged;
            _configuration = _subsonicService.Configuration;
            if (_configuration != null) _configuration.PropertyChanged += OnConfigurationChanged;
        }

        private async void OnServiceChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != "Configuration") return;
            ObserveConfiguration();
            NotifyOfPropertyChange(() => Entries);
            await RefreshAsync();
        }

        private async void OnConfigurationChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == "BaseUrl" || args.PropertyName == "Username")
            {
                NotifyOfPropertyChange(() => Entries);
                await RefreshAsync();
            }
        }
    }
}
