namespace Mixdrome.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Client.Common.Models.Navidrome;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.Navidrome;
    using Windows.Storage;

    public interface IOfflineLibraryService : INotifyPropertyChanged
    {
        BindableCollection<OfflineDownloadItem> Entries { get; }
        Task RefreshAsync();
        Task EnqueueAsync(NavidromeApi api, IEnumerable<Song> songs);
        Task RetryAsync(OfflineDownloadItem item);
        void Cancel(OfflineDownloadItem item);
        void CancelAll();
        Task DeleteAsync(IEnumerable<OfflineDownloadItem> items);
        Task<IList<PlaylistItem>> CreateQueueAsync(IEnumerable<OfflineDownloadItem> items);
        Task<StorageFile> GetOriginalAsync(OfflineDownloadItem item);
        Task<IList<LyricVariant>> GetLyricsAsync(PlaylistItem item);
        Task<Song> GetSongAsync(PlaylistItem item);
    }

    public sealed class OfflineLibraryService : PropertyChangedBase, IOfflineLibraryService
    {
        private const string RootName = "navidrome-offline";
        private const string CatalogName = "catalog.xml";
        private readonly INavidromeSessionService _session;
        private readonly SemaphoreSlim _catalogGate = new SemaphoreSlim(1, 1);
        private readonly Queue<OfflineDownloadItem> _pending = new Queue<OfflineDownloadItem>();
        private readonly List<OfflineDownloadItem> _jobs = new List<OfflineDownloadItem>();
        private readonly HttpClient _http;
        private bool _running;

        public OfflineLibraryService(INavidromeSessionService session)
            : this(session, new HttpClient { Timeout = TimeSpan.FromMinutes(30) }) { }

        internal OfflineLibraryService(INavidromeSessionService session, HttpClient http)
        {
            _session = session; _http = http; Entries = new BindableCollection<OfflineDownloadItem>();
            session.PropertyChanged += SessionChanged;
        }
        public BindableCollection<OfflineDownloadItem> Entries { get; private set; }
        private async void SessionChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != "AccountKey") return;
            foreach (var job in _jobs.Where(item => item.AccountKey != _session.AccountKey && item.CanCancel)) Cancel(job);
            Entries.Clear();
            try { await RefreshAsync(); } catch (Exception) { /* The active downloads page reports catalog errors on reload. */ }
        }

        public async Task RefreshAsync()
        {
            var account = _session.AccountKey;
            if (string.IsNullOrEmpty(account)) { Entries.Clear(); return; }
            await _catalogGate.WaitAsync();
            try
            {
                var folder = await Folder(account);
                var catalog = await Read(folder, account);
                var entries = new List<OfflineDownloadItem>();
                foreach (var record in catalog.Songs)
                {
                    var job = _jobs.LastOrDefault(item => item.AccountKey == account && item.Song.Id == record.Song.Id);
                    var entry = job ?? new OfflineDownloadItem { AccountKey = account, Song = record.Song };
                    if (job == null || !job.CanCancel)
                    {
                        entry.Record = record;
                        entry.OriginalAvailable = await HasFile(folder, record.OriginalFile, record.OriginalSize);
                        entry.PlaybackAvailable = record.PlaybackFile != null && await HasFile(folder, record.PlaybackFile, record.PlaybackSize);
                        entry.ImageUrl = record.ArtworkFile == null ? Client.Common.Services.SubsonicService.CoverArtPlaceholder : LocalUrl(account, record.ArtworkFile).AbsoluteUri;
                        if (job == null || entry.State == OfflineDownloadState.Completed || entry.State == OfflineDownloadState.Missing)
                        {
                            entry.Error = record.CacheError;
                            entry.State = entry.OriginalAvailable ? OfflineDownloadState.Completed : OfflineDownloadState.Missing;
                        }
                        else entry.Changed();
                    }
                    entries.Add(entry);
                }
                entries.AddRange(_jobs.Where(item => item.AccountKey == account && entries.All(entry => entry.Song.Id != item.Song.Id)));
                if (account != _session.AccountKey) return;
                Entries.Clear(); Entries.AddRange(entries);
                NotifyOfPropertyChange(() => Entries);
            }
            finally { _catalogGate.Release(); }
        }

        public async Task EnqueueAsync(NavidromeApi api, IEnumerable<Song> songs)
        {
            if (api.AccountKey != _session.AccountKey) throw new OperationCanceledException();
            if (_session.User == null || !_session.User.DownloadRole) throw new InvalidOperationException(NavidromeText.Get("DownloadingUnavailable"));
            await RefreshAsync();
            if (api.AccountKey != _session.AccountKey) throw new OperationCanceledException();
            foreach (var song in songs.GroupBy(item => item.Id).Select(group => group.First()))
            {
                var existing = Entries.FirstOrDefault(item => item.Song.Id == song.Id);
                if (existing != null && (existing.CanCancel || (existing.OriginalAvailable && existing.PlaybackAvailable))) continue;
                if (existing != null) { Entries.Remove(existing); _jobs.Remove(existing); }
                var job = new OfflineDownloadItem { AccountKey = api.AccountKey, Song = song,
                    Api = api, Cancellation = new CancellationTokenSource(), Finished = new TaskCompletionSource<bool>(),
                    State = OfflineDownloadState.Queued, StructuredLyrics = _session.Capabilities.Supports("songLyrics"),
                    Record = existing == null ? null : existing.Record, OriginalAvailable = existing != null && existing.OriginalAvailable,
                    PlaybackAvailable = existing != null && existing.PlaybackAvailable, ImageUrl = existing == null ? api.CoverArtUrl(song.CoverArt) : existing.ImageUrl };
                _jobs.Add(job); _pending.Enqueue(job); Entries.Add(job);
            }
            NotifyOfPropertyChange(() => Entries);
            StartWorker();
        }

        public async Task RetryAsync(OfflineDownloadItem item)
        {
            RequireAccount(item); await _session.ConnectAsync();
            RequireAccount(item); await EnqueueAsync(_session.CreateApi(), new[] { item.Song });
        }
        public void Cancel(OfflineDownloadItem item) { if (item.Cancellation != null && item.CanCancel) item.Cancellation.Cancel(); }
        public void CancelAll() { foreach (var item in _jobs.Where(item => item.AccountKey == _session.AccountKey && item.CanCancel)) Cancel(item); }
        private async void StartWorker()
        {
            if (_running) return;
            _running = true;
            try
            {
                while (_pending.Count > 0)
                {
                    var job = _pending.Dequeue();
                    try { await Download(job); job.State = OfflineDownloadState.Completed; }
                    catch (OperationCanceledException) { job.State = OfflineDownloadState.Cancelled; }
                    catch (Exception exception) { job.Error = exception.Message; job.State = OfflineDownloadState.Failed; }
                    finally { job.Finished.TrySetResult(true); job.Cancellation.Dispose(); job.Cancellation = null; NotifyOfPropertyChange(() => Entries); }
                }
            }
            finally { _running = false; }
        }

        private async Task Download(OfflineDownloadItem job)
        {
            var cancellation = job.Cancellation.Token;
            RequireAccount(job); cancellation.ThrowIfCancellationRequested();
            var folder = await Folder(job.AccountKey);
            var token = Guid.NewGuid().ToString("N");
            var created = new List<string>();
            var committed = false;
            try
            {
                job.State = OfflineDownloadState.Downloading; job.Error = null;
                job.Song = await job.Api.GetSong(job.Song.Id).ReadAsync();
                cancellation.ThrowIfCancellationRequested(); RequireAccount(job); job.Changed();
                job.Stage = NavidromeText.Get("DownloadOriginal");
                var originalName = token + "-original." + OfflineCatalog.Suffix(job.Song.Suffix);
                var original = await Transfer(folder, originalName, job.Api.DownloadUri(job.Song.Id), job, cancellation, job.Song.Size);
                created.Add(originalName);
                var record = new DownloadedSong { Song = job.Song, OriginalFile = originalName, OriginalSize = original.Length,
                    CompletedUtc = DateTime.UtcNow };
                var warnings = new List<string>();
                if (original.WindowsCompatible) { record.PlaybackFile = originalName; record.PlaybackSize = original.Length; }
                else
                {
                    job.Stage = NavidromeText.Get("DownloadCompatible");
                    var playbackName = token + "-playback.mp3";
                    try
                    {
                        var playback = await Transfer(folder, playbackName, job.Api.StreamUri(job.Song.Id, "mp3"), job, cancellation, 0, true);
                        created.Add(playbackName); record.PlaybackFile = playbackName; record.PlaybackSize = playback.Length;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception exception) { warnings.Add(NavidromeText.Get("OfflinePlaybackUnavailable") + " " + exception.Message); }
                }
                cancellation.ThrowIfCancellationRequested(); RequireAccount(job);
                job.Stage = NavidromeText.Get("DownloadArtwork");
                Uri art;
                if (!string.IsNullOrEmpty(job.Song.CoverArt) && Uri.TryCreate(job.Api.CoverArtUrl(job.Song.CoverArt), UriKind.Absolute, out art) &&
                    (art.Scheme == "http" || art.Scheme == "https"))
                {
                    var name = token + "-cover.jpg";
                    try { await Transfer(folder, name, art, job, cancellation); created.Add(name); record.ArtworkFile = name; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception exception) { warnings.Add(NavidromeText.Get("ArtworkCacheFailed") + " " + exception.Message); }
                }
                job.Stage = NavidromeText.Get("DownloadLyrics");
                try { record.Lyrics = (await (job.StructuredLyrics ? job.Api.GetLyrics(job.Song.Id, true) : job.Api.GetLegacyLyrics(job.Song.Artist, job.Song.Title)).ReadAsync()).ToList(); }
                catch (Exception exception) { warnings.Add(NavidromeText.Get("LyricsCacheFailed") + " " + exception.Message); }
                cancellation.ThrowIfCancellationRequested(); RequireAccount(job);
                record.CacheError = warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings);
                DownloadedSong previous;
                await _catalogGate.WaitAsync();
                try
                {
                    var catalog = await Read(folder, job.AccountKey);
                    previous = catalog.Songs.FirstOrDefault(item => item.Song.Id == job.Song.Id);
                    catalog.Songs.RemoveAll(item => item.Song.Id == job.Song.Id); catalog.Songs.Add(record);
                    await Write(folder, catalog); committed = true;
                }
                finally { _catalogGate.Release(); }
                job.Record = record; job.OriginalAvailable = true; job.PlaybackAvailable = record.PlaybackFile != null;
                job.ImageUrl = record.ArtworkFile == null ? Client.Common.Services.SubsonicService.CoverArtPlaceholder : LocalUrl(job.AccountKey, record.ArtworkFile).AbsoluteUri;
                job.Error = record.CacheError; job.Changed();
                if (previous != null)
                    foreach (var name in Files(previous))
                    {
                        try { await DeleteIfExists(folder, name); }
                        catch (Exception exception) { job.Error = NavidromeText.Get("DownloadCleanupFailed") + " " + exception.Message; }
                    }
            }
            finally { if (!committed) foreach (var name in created) await DeleteIfExists(folder, name); }
        }

        private async Task<DownloadPayload> Transfer(StorageFolder folder, string name, Uri uri, OfflineDownloadItem job,
            CancellationToken cancellation, long expectedLength = 0, bool requireMp3 = false)
        {
            if (!OfflineCatalog.IsFileName(name)) throw new InvalidDataException(NavidromeText.Get("OfflineDataInvalid"));
            var file = await folder.CreateFileAsync(name + ".partial", CreationCollisionOption.ReplaceExisting);
            var complete = false;
            try
            {
                DownloadPayload payload;
                using (var stream = await file.OpenStreamForWriteAsync())
                    payload = await MediaDownload.CopyAsync(_http, uri, stream, cancellation, new Progress<DownloadProgress>(job.Report), expectedLength, requireMp3);
                cancellation.ThrowIfCancellationRequested();
                await file.RenameAsync(name, NameCollisionOption.FailIfExists); complete = true;
                return payload;
            }
            finally { if (!complete) await file.DeleteAsync(); }
        }

        public async Task<IList<PlaylistItem>> CreateQueueAsync(IEnumerable<OfflineDownloadItem> items)
        {
            var queue = new List<PlaylistItem>();
            foreach (var item in items)
            {
                RequireAccount(item);
                var record = item.Record;
                var folder = await Folder(item.AccountKey);
                if (record == null || record.PlaybackFile == null || !await HasFile(folder, record.PlaybackFile, record.PlaybackSize))
                    throw new InvalidOperationException(NavidromeText.Get("OfflineFileMissing"));
                var song = record.Song;
                queue.Add(new PlaylistItem { AccountKey = item.AccountKey, SongId = song.Id, AlbumId = song.AlbumId, ArtistId = song.ArtistId,
                    CoverArtId = song.CoverArt, Title = song.Title, Artist = string.IsNullOrWhiteSpace(song.DisplayArtist) ? song.Artist : song.DisplayArtist,
                    ReplayGain = song.ReplayGain, Duration = song.Duration, UserRating = song.UserRating,
                    IsStarred = !string.IsNullOrEmpty(song.Starred), Type = PlaylistItemTypeEnum.Audio,
                    Uri = LocalUrl(item.AccountKey, record.PlaybackFile), CoverArtUrl = item.ImageUrl });
            }
            if (queue.Any(item => item.AccountKey != _session.AccountKey)) throw new OperationCanceledException();
            return queue;
        }

        public async Task<StorageFile> GetOriginalAsync(OfflineDownloadItem item)
        {
            RequireAccount(item);
            if (item.Record == null) throw new InvalidOperationException(NavidromeText.Get("OfflineFileMissing"));
            var folder = await Folder(item.AccountKey);
            if (!await HasFile(folder, item.Record.OriginalFile, item.Record.OriginalSize)) throw new InvalidOperationException(NavidromeText.Get("OfflineFileMissing"));
            return await folder.GetFileAsync(item.Record.OriginalFile);
        }

        public async Task<IList<LyricVariant>> GetLyricsAsync(PlaylistItem item)
        {
            if (item == null || item.AccountKey != _session.AccountKey) return null;
            await _catalogGate.WaitAsync();
            try
            {
                var catalog = await Read(await Folder(item.AccountKey), item.AccountKey);
                var record = catalog.Songs.FirstOrDefault(entry => entry.Song.Id == item.SongId);
                return record == null ? null : record.Lyrics;
            }
            finally { _catalogGate.Release(); }
        }

        public async Task<Song> GetSongAsync(PlaylistItem item)
        {
            if (item == null || item.AccountKey != _session.AccountKey) return null;
            await _catalogGate.WaitAsync();
            try
            {
                var catalog = await Read(await Folder(item.AccountKey), item.AccountKey);
                var record = catalog.Songs.FirstOrDefault(entry => entry.Song.Id == item.SongId);
                return record == null ? null : record.Song;
            }
            finally { _catalogGate.Release(); }
        }

        public async Task DeleteAsync(IEnumerable<OfflineDownloadItem> items)
        {
            var selection = items.ToList();
            foreach (var item in selection) { RequireAccount(item); Cancel(item); }
            foreach (var item in selection) if (item.Finished != null) await item.Finished.Task;
            var account = _session.AccountKey;
            if (selection.Any(item => item.AccountKey != account)) throw new OperationCanceledException();
            await _catalogGate.WaitAsync();
            try
            {
                var folder = await Folder(account); var catalog = await Read(folder, account);
                var records = catalog.Songs.Where(record => selection.Any(item => item.Song.Id == record.Song.Id)).ToList();
                catalog.Songs.RemoveAll(record => records.Contains(record)); await Write(folder, catalog);
                foreach (var record in records) foreach (var name in Files(record)) await DeleteIfExists(folder, name);
                foreach (var item in selection) { _jobs.Remove(item); Entries.Remove(item); }
                NotifyOfPropertyChange(() => Entries);
            }
            finally { _catalogGate.Release(); }
        }

        private void RequireAccount(OfflineDownloadItem item) { if (item.AccountKey != _session.AccountKey) throw new OperationCanceledException(); }
        private static Uri LocalUrl(string account, string name)
        {
            if (!OfflineCatalog.IsFileName(name)) throw new InvalidDataException(NavidromeText.Get("OfflineDataInvalid"));
            return new Uri("ms-appdata:///local/" + RootName + "/" + NavidromeSessionService.Hash(account) + "/" + name);
        }
        private static async Task<StorageFolder> Folder(string account)
        {
            if (string.IsNullOrEmpty(account)) throw new InvalidOperationException(NavidromeText.Get("ConfigureServer"));
            var root = await ApplicationData.Current.LocalFolder.CreateFolderAsync(RootName, CreationCollisionOption.OpenIfExists);
            return await root.CreateFolderAsync(NavidromeSessionService.Hash(account), CreationCollisionOption.OpenIfExists);
        }
        private static async Task<OfflineCatalog> Read(StorageFolder folder, string account)
        {
            try { var catalog = LocalXml.Deserialize<OfflineCatalog>(await FileIO.ReadTextAsync(await folder.GetFileAsync(CatalogName))); catalog.Validate(account); return catalog; }
            catch (FileNotFoundException) { return new OfflineCatalog { AccountKey = account }; }
        }
        private static async Task Write(StorageFolder folder, OfflineCatalog catalog)
        {
            catalog.Validate(catalog.AccountKey);
            var file = await folder.CreateFileAsync(CatalogName + ".pending", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, LocalXml.Serialize(catalog)); await file.RenameAsync(CatalogName, NameCollisionOption.ReplaceExisting);
        }
        private static async Task<bool> HasFile(StorageFolder folder, string name, long size)
        {
            if (!OfflineCatalog.IsFileName(name)) throw new InvalidDataException(NavidromeText.Get("OfflineDataInvalid"));
            try { return (await (await folder.GetFileAsync(name)).GetBasicPropertiesAsync()).Size == (ulong)size; }
            catch (FileNotFoundException) { return false; }
        }
        private static IEnumerable<string> Files(DownloadedSong record) { return new[] { record.OriginalFile, record.PlaybackFile, record.ArtworkFile }.Where(name => name != null).Distinct(); }
        private static async Task DeleteIfExists(StorageFolder folder, string name)
        {
            if (!OfflineCatalog.IsFileName(name)) throw new InvalidDataException(NavidromeText.Get("OfflineDataInvalid"));
            try { await (await folder.GetFileAsync(name)).DeleteAsync(); } catch (FileNotFoundException) { }
        }
    }
}
