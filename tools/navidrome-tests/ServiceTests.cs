using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using Client.Common.Models;
using Client.Common.Models.Navidrome;
using Client.Common.Services;
using Client.Common.Services.DataStructures.SubsonicService;
using Client.Common.Services.Navidrome;
using Subsonic8.Navidrome;
using Windows.Storage;

public static class NavidromeServiceTests
{
    [DataContract] private sealed class Fixture
    {
        [DataMember(Name = "port")] public int Port { get; set; }
        [DataMember(Name = "username")] public string Username { get; set; }
        [DataMember(Name = "password")] public string Password { get; set; }
    }
    private static int _assertions;
    private static void Check(bool value, string message) { _assertions++; if (!value) throw new Exception(message); }
    public static int Main(string[] args)
    {
        try
        {
            using (var pump = new Pump()) pump.Run(() => Run(args[0], args[1]));
            Console.WriteLine("PASS: " + _assertions + " production-service assertions with CLR storage/event adapters and a real Navidrome fixture. Native UI/audio remain unverified.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) { Windows.UI.Xaml.DispatcherTimer.Pulse(); await Task.Delay(50); }
        Check(condition(), "An asynchronous operation finishes within its test deadline.");
    }
    private static async Task Run(string statePath, string output)
    {
        Fixture fixture;
        using (var stream = File.OpenRead(statePath)) fixture = (Fixture)new DataContractJsonSerializer(typeof(Fixture)).ReadObject(stream);
        ApplicationData.Current.UseRoot(output);
        var configuration = new SubsonicServiceConfiguration { BaseUrl = "http://127.0.0.1:" + fixture.Port + "/", Username = fixture.Username, Password = fixture.Password };
        var subsonic = new SubsonicService { Configuration = configuration };
        var session = new NavidromeSessionService(subsonic); await session.ConnectAsync();
        Check(session.IsConnected && session.User.DownloadRole, "Production sessions negotiate download permission.");
        session.SelectedFolder = session.Folders.Last(); var selectedFolderId = session.FolderId;
        await session.ConnectAsync(true);
        Check(session.IsConnected && session.User.AdminRole && session.FolderId == selectedFolderId, "Explicit reconnect refreshes actual roles and restores the selected accessible library.");
        Check(session.UserError == null && session.CapabilityError == null, "Successful optional server responses have no hidden connection warnings.");
        var api = session.CreateApi();
        var songs = (await api.Search(new SearchQuery { Query = "", ArtistCount = 0, AlbumCount = 0, SongCount = 3 }).ReadAsync()).Songs.ToList();
        var offline = new OfflineLibraryService(session);
        await offline.EnqueueAsync(api, new[] { songs[0], songs[0] });
        await Until(() => offline.Entries.All(entry => !entry.CanCancel));
        var item = offline.Entries.Single();
        Check(item.State == OfflineDownloadState.Completed && item.CanPlay && item.OriginalAvailable, "Original PCM downloads become playable only after catalog completion.");
        var original = await offline.GetOriginalAsync(item);
        var originalBytes = File.ReadAllBytes(original.Path);
        var queue = await offline.CreateQueueAsync(new[] { item });
        Check(queue.Count == 1 && queue[0].Uri.Scheme == "ms-appdata" && queue[0].SongId == item.Song.Id, "Offline queues resolve completed local files without stream URLs.");
        var catalogPath = Path.Combine(Path.GetDirectoryName(original.Path), "catalog.xml");
        var savedXml = File.ReadAllText(catalogPath);
        var catalog = LocalXml.Deserialize<OfflineCatalog>(savedXml); catalog.Validate(api.AccountKey);
        Check(catalog.Songs.Count == 1 && !savedXml.Contains(configuration.EncodedPassword) && !savedXml.Contains("/rest/stream") && !savedXml.Contains("/rest/getCoverArt"),
            "Completed catalogs retain metadata while excluding credential-bearing media URLs.");
        Check(!Directory.GetFiles(output, "*.partial", SearchOption.AllDirectories).Any(), "Completed downloads leave no partial files.");
        configuration.Password = "disconnected-fixture";
        Check(!session.IsConnected && session.Capabilities == null && session.User == null, "Credential changes invalidate the negotiated session immediately.");
        await offline.RefreshAsync(); item = offline.Entries.Single();
        Check((await offline.CreateQueueAsync(new[] { item })).Single().Uri.Scheme == "ms-appdata", "Offline playback does not require a server handshake.");
        Check(File.ReadAllBytes((await offline.GetOriginalAsync(item)).Path).SequenceEqual(originalBytes), "Original export stays byte-identical while disconnected.");
        Check(await offline.GetLyricsAsync(queue[0]) != null, "Cached lyric variants are available without connecting.");
        var cachedSong = await offline.GetSongAsync(queue[0]);
        Check(cachedSong.Id == songs[0].Id && cachedSong.Album == songs[0].Album && cachedSong.ArtistId == songs[0].ArtistId,
            "The offline player retrieves real album and artist metadata without a connection.");
        configuration.Password = fixture.Password; await session.ConnectAsync(); api = session.CreateApi();
        await offline.EnqueueAsync(api, new[] { songs[1] });
        var cancelled = offline.Entries.Single(entry => entry.Song.Id == songs[1].Id); offline.Cancel(cancelled);
        await Until(() => !cancelled.CanCancel);
        Check(cancelled.State == OfflineDownloadState.Cancelled && cancelled.Record == null, "Cancelled downloads cannot become offline records.");
        Check(!Directory.GetFiles(output, "*.partial", SearchOption.AllDirectories).Any(), "Cancellation cleans partial files.");
        File.Delete(original.Path);
        await offline.RefreshAsync(); item = offline.Entries.Single(entry => entry.Song.Id == songs[0].Id);
        Check(item.State == OfflineDownloadState.Missing && !item.CanPlay, "Missing completed files are represented as unavailable.");
        var missing = false; try { await offline.CreateQueueAsync(new[] { item }); } catch (InvalidOperationException) { missing = true; }
        Check(missing, "Missing files cannot enter the offline playback queue.");
        await offline.RetryAsync(item); await Until(() => offline.Entries.All(entry => !entry.CanCancel));
        item = offline.Entries.Single(entry => entry.Song.Id == songs[0].Id);
        Check(item.CanPlay && File.ReadAllBytes((await offline.GetOriginalAsync(item)).Path).SequenceEqual(originalBytes), "Retry restores actual files without duplicating the catalog song.");
        savedXml = File.ReadAllText(catalogPath);
        File.WriteAllText(catalogPath, "invalid fixture catalog");
        var corrupt = false; try { await offline.RefreshAsync(); } catch (InvalidOperationException) { corrupt = true; }
        Check(corrupt && File.ReadAllText(catalogPath) == "invalid fixture catalog", "Corrupt catalogs are reported and preserved instead of overwritten.");
        File.WriteAllText(catalogPath, savedXml);
        configuration.Username += "-other"; await offline.RefreshAsync();
        Check(offline.Entries.Count == 0, "Other accounts cannot see the existing offline library.");
        var wrongAccount = false; try { await offline.CreateQueueAsync(new[] { item }); } catch (OperationCanceledException) { wrongAccount = true; }
        Check(wrongAccount && await offline.GetLyricsAsync(queue[0]) == null, "Cross-account queue/lyric access is rejected.");
        Check(await offline.GetSongAsync(queue[0]) == null, "The offline player cannot retrieve metadata from another account.");
        configuration.Username = fixture.Username; await session.ConnectAsync(); await offline.RefreshAsync();
        item = offline.Entries.Single(entry => entry.Song.Id == songs[0].Id);
        var playbackItem = (await offline.CreateQueueAsync(new[] { item })).Single();
        var audio = new Audio { Item = playbackItem };
        var activity = new PlaybackActivityService(session, audio);
        var before = (await api.GetSong(playbackItem.SongId).ReadAsync()).PlayCount;
        configuration.Password = "offline-fixture";
        audio.Start(); await Task.Delay(1600); audio.Pause(); Windows.UI.Xaml.DispatcherTimer.Pulse();
        await Until(() => activity.PendingCount == 1);
        audio.Start(); audio.Pause(); audio.Stop(); Windows.UI.Xaml.DispatcherTimer.Pulse();
        Check(activity.PendingCount == 1, "Pause/resume and repeated stop cannot duplicate a qualifying occurrence.");
        audio.Start(); await Task.Delay(1600); audio.Stop(); Windows.UI.Xaml.DispatcherTimer.Pulse();
        await Until(() => activity.PendingCount == 2);
        var pendingPath = Directory.GetFiles(output, "pending-listens-*.xml").Single();
        var pending = LocalXml.Deserialize<PendingListening>(File.ReadAllText(pendingPath)); pending.Validate(api.AccountKey);
        Check(pending.Entries.Count == 2 && pending.Entries.Select(entry => entry.OccurrenceId).Distinct().Count() == 2,
            "Separate offline listens retain distinct occurrence IDs and UTC times.");
        configuration.Password = fixture.Password; await session.ConnectAsync(); await Until(() => activity.PendingCount == 0);
        Check((await api.GetSong(playbackItem.SongId).ReadAsync()).PlayCount == before + 2, "A restored production session synchronizes both pending listens once.");
        audio.Stop(); Windows.UI.Xaml.DispatcherTimer.Pulse(); await activity.SyncAsync();
        Check((await api.GetSong(playbackItem.SongId).ReadAsync()).PlayCount == before + 2, "Reprocessing stopped or acknowledged occurrences does not submit again.");
        await offline.DeleteAsync(offline.Entries.ToList()); await offline.RefreshAsync();
        Check(offline.Entries.Count == 0 && LocalXml.Deserialize<OfflineCatalog>(File.ReadAllText(catalogPath)).Songs.Count == 0,
            "Deleting downloads removes their catalog records and browsing entries.");
        await activity.SuspendAsync();
        var flac = new byte[(int)songs[1].Size];
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("fLaC"), flac, 4);
        using (var http = new System.Net.Http.HttpClient(new BinaryResponses { Original = flac, Compatible = true }))
        {
            var transcoded = new OfflineLibraryService(session, http);
            await transcoded.EnqueueAsync(session.CreateApi(), new[] { songs[1] }); await Until(() => transcoded.Entries.All(entry => !entry.CanCancel));
            var result = transcoded.Entries.Single();
            Check(result.CanPlay && result.Record.OriginalFile != result.Record.PlaybackFile && result.Record.PlaybackFile.EndsWith("-playback.mp3"),
                "Synthetic incompatible originals select a separate MP3 playback copy.");
            Check(File.ReadAllBytes((await transcoded.GetOriginalAsync(result)).Path).SequenceEqual(flac), "A compatible playback copy cannot replace original export bytes.");
            await transcoded.DeleteAsync(new[] { result });
        }
        using (var http = new System.Net.Http.HttpClient(new BinaryResponses { Original = flac, Compatible = false }))
        {
            var unsupported = new OfflineLibraryService(session, http);
            await unsupported.EnqueueAsync(session.CreateApi(), new[] { songs[1] }); await Until(() => unsupported.Entries.All(entry => !entry.CanCancel));
            var result = unsupported.Entries.Single();
            Check(result.State == OfflineDownloadState.Completed && result.OriginalAvailable && !result.CanPlay && result.CanRetry && result.HasError,
                "Transcoding errors preserve original export while exposing an unavailable offline player and a retry.");
            Check(!Directory.GetFiles(output, "*.partial", SearchOption.AllDirectories).Any(), "Rejected compatible streams leave no partial file.");
            await unsupported.DeleteAsync(new[] { result });
        }
    }
    private sealed class Audio : Subsonic8.AudioPlayback.IAudioPlayerViewModel
    {
        public PlaylistItem Item;
        public PlaylistItem CurrentItem { get { return Item; } }
        public bool IsActuallyPlaying { get; private set; }
        public double PlaybackRate { get { return 2; } }
        public TimeSpan LastStoppedPosition { get; private set; }
        public TimeSpan GetCurrentPosition() { return TimeSpan.FromSeconds(5); }
        public TimeSpan GetDuration() { return TimeSpan.FromSeconds(6); }
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<EventArgs> PlaybackStarted, PlaybackPaused, PlaybackStoped;
        public event EventHandler PlaybackSeeked;
        private void Changed() { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("IsActuallyPlaying")); }
        public void Start() { IsActuallyPlaying = true; Changed(); if (PlaybackStarted != null) PlaybackStarted(this, EventArgs.Empty); }
        public void Pause() { IsActuallyPlaying = false; Changed(); if (PlaybackPaused != null) PlaybackPaused(this, EventArgs.Empty); }
        public void Stop() { LastStoppedPosition = GetCurrentPosition(); IsActuallyPlaying = false; Changed(); if (PlaybackStoped != null) PlaybackStoped(this, EventArgs.Empty); }
        public void Seek() { if (PlaybackSeeked != null) PlaybackSeeked(this, EventArgs.Empty); }
    }
    private sealed class Pump : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<Action> _work = new BlockingCollection<Action>();
        public override void Post(SendOrPostCallback callback, object state) { _work.Add(() => callback(state)); }
        public void Run(Func<Task> action)
        {
            SetSynchronizationContext(this);
            var task = action();
            while (!task.IsCompleted) { Action next; if (_work.TryTake(out next, 100)) next(); }
            task.GetAwaiter().GetResult(); SetSynchronizationContext(null);
        }
        public void Dispose() { _work.Dispose(); }
    }
    private sealed class BinaryResponses : System.Net.Http.HttpMessageHandler
    {
        public byte[] Original;
        public bool Compatible;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var download = request.RequestUri.AbsolutePath.EndsWith("/download.view");
            var bytes = download ? Original : Compatible ? new byte[] { 255, 251, 144, 100, 1, 2 } : System.Text.Encoding.UTF8.GetBytes("<error/>");
            var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(download ? "application/octet-stream" : Compatible ? "audio/mpeg" : "application/xml");
            return Task.FromResult(response);
        }
    }
}
