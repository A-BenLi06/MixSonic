namespace Subsonic8.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Client.Common.Services.Navidrome;
    using Subsonic8.AudioPlayback;
    using Windows.Storage;
    using Windows.UI.Xaml;

    public interface IPlaybackActivityService : INotifyPropertyChanged
    {
        string Error { get; }
        int PendingCount { get; }
        Task SyncAsync(bool force = false);
        Task SuspendAsync();
    }

    public sealed class PlaybackActivityService : PropertyChangedBase, IPlaybackActivityService
    {
        private sealed class PlayingOccurrence
        {
            public PlaylistItem Item;
            public ListeningCounter Counter;
            public string Id = Guid.NewGuid().ToString("N");
            public DateTime StartedUtc = DateTime.UtcNow;
            public bool Stored, Ended, PresenceStarted;
        }
        private readonly INavidromeSessionService _session;
        private readonly IAudioPlayerViewModel _audio;
        private readonly SemaphoreSlim _storageGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _presenceGate = new SemaphoreSlim(1, 1);
        private readonly List<PlayingOccurrence> _occurrences = new List<PlayingOccurrence>();
        private readonly DispatcherTimer _timer;
        private readonly string _clientId;
        private PlayingOccurrence _current, _reported;
        private double _lastReport, _lastSync, _presenceRetry, _syncRetry, _storageRetry;
        private bool _syncing, _ticking, _periodicPending;

        public PlaybackActivityService(INavidromeSessionService session, IAudioPlayerViewModel audio)
        {
            _session = session; _audio = audio;
            var settings = ApplicationData.Current.LocalSettings.Values;
            _clientId = settings.ContainsKey("PlaybackClientId") ? settings["PlaybackClientId"] as string : null;
            if (string.IsNullOrEmpty(_clientId)) { _clientId = Guid.NewGuid().ToString("N"); settings["PlaybackClientId"] = _clientId; }
            audio.PlaybackStarted += Started;
            audio.PlaybackPaused += Paused;
            audio.PlaybackStoped += Stopped;
            audio.PlaybackSeeked += Seeked;
            audio.PropertyChanged += AudioChanged;
            session.PropertyChanged += SessionChanged;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Tick;
            // Also retry persisted offline listens after a connection or failed request.
            _timer.Start();
            Application.Current.Resuming += (sender, args) => _timer.Start();
        }

        public string Error { get; private set; }
        public int PendingCount { get; private set; }
        private static double Now { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }
        private void SetError(string error) { Error = error; NotifyOfPropertyChange(() => Error); }
        private void SetCount(string account, int count)
        {
            if (account != _session.AccountKey) return;
            PendingCount = count; NotifyOfPropertyChange(() => PendingCount);
        }

        private void Started(object sender, EventArgs args)
        {
            var item = _audio.CurrentItem;
            if (item == null || string.IsNullOrEmpty(item.SongId) || string.IsNullOrEmpty(item.AccountKey)) return;
            if (_current == null || _current.Ended || !ReferenceEquals(_current.Item, item))
            {
                _current = new PlayingOccurrence { Item = item, Counter = new ListeningCounter { DurationSeconds = _audio.GetDuration().TotalSeconds } };
                _occurrences.Add(_current);
            }
            Observe(); _timer.Start(); SendPresence(_current, "playing", false);
        }
        private void Paused(object sender, EventArgs args) { Observe(); if (_current != null && !_current.Ended) SendPresence(_current, "paused", false); }
        private void Seeked(object sender, EventArgs args) { Observe(); if (_current != null && !_current.Ended) SendPresence(_current, _audio.IsActuallyPlaying ? "playing" : "paused", false); }
        private async void Stopped(object sender, EventArgs args)
        {
            if (_current == null || _current.Ended) return;
            Observe(); _current.Counter.Observe(Now, false, _audio.PlaybackRate); _current.Ended = true;
            var occurrence = _current;
            SendPresence(occurrence, "stopped", false);
            await StoreQualifiedAsync(occurrence);
        }
        private void AudioChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == "IsActuallyPlaying" || args.PropertyName == "PlaybackRate") Observe();
        }
        private async void SessionChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == "AccountKey") { PendingCount = 0; NotifyOfPropertyChange(() => PendingCount); _presenceRetry = _syncRetry = 0; }
            if (args.PropertyName == "IsConnected" && _session.IsConnected) { _presenceRetry = _syncRetry = 0; await SyncAsync(); }
        }
        private void Observe()
        {
            if (_current == null || _current.Ended) return;
            var sameItem = ReferenceEquals(_current.Item, _audio.CurrentItem);
            if (sameItem) _current.Counter.DurationSeconds = _audio.GetDuration().TotalSeconds;
            _current.Counter.Observe(Now, _audio.IsActuallyPlaying && sameItem, _audio.PlaybackRate);
        }
        private async void Tick(object sender, object args)
        {
            if (_ticking) return;
            _ticking = true;
            try
            {
                Observe();
                foreach (var occurrence in _occurrences.ToList()) await StoreQualifiedAsync(occurrence);
                _occurrences.RemoveAll(item => item.Ended && (item.Stored || !item.Counter.Qualified));
                if (_current != null && !_current.Ended && Now - _lastReport >= 10)
                { _lastReport = Now; SendPresence(_current, _audio.IsActuallyPlaying ? "playing" : "paused", true); }
                if (Now - _lastSync >= 10) { _lastSync = Now; await SyncAsync(); }
            }
            catch (Exception exception) { SetError(exception.Message); }
            finally { _ticking = false; }
        }
        public async Task SuspendAsync()
        {
            Observe(); _audio.Pause();
            if (_current != null && !_current.Ended) _current.Counter.Observe(Now, false, _audio.PlaybackRate);
            _timer.Stop();
            foreach (var occurrence in _occurrences.ToList()) await StoreQualifiedAsync(occurrence);
        }
        private async Task StoreQualifiedAsync(PlayingOccurrence occurrence)
        {
            if (!occurrence.Counter.Qualified || occurrence.Stored || Now < _storageRetry) return;
            await _storageGate.WaitAsync();
            try
            {
                if (occurrence.Stored) return;
                var catalog = await Read(occurrence.Item.AccountKey);
                if (!catalog.Entries.Any(entry => entry.OccurrenceId == occurrence.Id))
                    catalog.Entries.Add(new PendingListen { OccurrenceId = occurrence.Id, SongId = occurrence.Item.SongId, StartedUtc = occurrence.StartedUtc });
                await Write(catalog); occurrence.Stored = true; SetCount(catalog.AccountKey, catalog.Entries.Count);
            }
            catch (Exception exception) { _storageRetry = Now + 30; SetError(exception.Message); }
            finally { _storageGate.Release(); }
        }
        private async void SendPresence(PlayingOccurrence occurrence, string state, bool periodic)
        {
            if (!_session.IsConnected || occurrence.Item.AccountKey != _session.AccountKey || Now < _presenceRetry || (periodic && _periodicPending)) return;
            if (periodic) _periodicPending = true;
            var api = _session.CreateApi();
            var enhanced = _session.Capabilities.Supports("playbackReport");
            var position = Math.Max(0, (long)(state == "stopped" ? _audio.LastStoppedPosition : _audio.GetCurrentPosition()).TotalMilliseconds);
            var rate = _audio.PlaybackRate;
            await _presenceGate.WaitAsync();
            try
            {
                if (!_session.IsConnected || api.AccountKey != _session.AccountKey || Now < _presenceRetry) return;
                if (state != "stopped" && (occurrence.Ended || !ReferenceEquals(_current, occurrence))) return;
                if (enhanced)
                {
                    if (state == "stopped" && !ReferenceEquals(_reported, occurrence)) return;
                    if (!occurrence.PresenceStarted && state != "stopped")
                    {
                        await api.ReportPlayback(occurrence.Item.SongId, position, "starting", rate, _clientId, true).ReadAsync();
                        occurrence.PresenceStarted = true; _reported = occurrence;
                    }
                    await api.ReportPlayback(occurrence.Item.SongId, position, state, rate, _clientId, true).ReadAsync();
                    if (state == "stopped") _reported = null;
                }
                else if (state == "playing") await api.Scrobble(occurrence.Item.SongId, false, new DateTimeOffset(occurrence.StartedUtc), (int)(position / 1000)).ReadAsync();
            }
            catch (Exception exception) { _presenceRetry = Now + 30; SetError(exception.Message); }
            finally { _presenceGate.Release(); if (periodic) _periodicPending = false; }
        }
        public async Task SyncAsync(bool force = false)
        {
            if (force) _syncRetry = 0;
            if (_syncing || !_session.IsConnected || Now < _syncRetry) return;
            _syncing = true;
            var api = _session.CreateApi();
            try
            {
                while (_session.IsConnected && api.AccountKey == _session.AccountKey)
                {
                    PendingListen entry;
                    await _storageGate.WaitAsync();
                    try { var catalog = await Read(api.AccountKey); SetCount(api.AccountKey, catalog.Entries.Count); entry = catalog.Entries.FirstOrDefault(); }
                    finally { _storageGate.Release(); }
                    if (entry == null || api.AccountKey != _session.AccountKey) break;
                    await api.Scrobble(entry.SongId, true, new DateTimeOffset(DateTime.SpecifyKind(entry.StartedUtc, DateTimeKind.Utc)), 0).ReadAsync();
                    await _storageGate.WaitAsync();
                    try
                    {
                        var catalog = await Read(api.AccountKey); catalog.Entries.RemoveAll(item => item.OccurrenceId == entry.OccurrenceId);
                        await Write(catalog); SetCount(api.AccountKey, catalog.Entries.Count);
                    }
                    finally { _storageGate.Release(); }
                }
                SetError(null);
            }
            catch (Exception exception) { _syncRetry = Now + 30; SetError(exception.Message); }
            finally { _syncing = false; }
        }
        private static string FileName(string account) { return "pending-listens-" + NavidromeSessionService.Hash(account) + ".xml"; }
        private static async Task<PendingListening> Read(string account)
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(FileName(account));
                var catalog = LocalXml.Deserialize<PendingListening>(await FileIO.ReadTextAsync(file));
                catalog.Validate(account); return catalog;
            }
            catch (FileNotFoundException) { return new PendingListening { AccountKey = account }; }
        }
        private static async Task Write(PendingListening catalog)
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(FileName(catalog.AccountKey) + ".pending", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, LocalXml.Serialize(catalog));
            await file.RenameAsync(FileName(catalog.AccountKey), NameCollisionOption.ReplaceExisting);
        }
    }
}
