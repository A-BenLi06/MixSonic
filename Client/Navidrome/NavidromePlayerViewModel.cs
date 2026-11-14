namespace Subsonic8.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.EventAggregatorMessages;
    using Client.Common.Models;
    using Client.Common.Models.Navidrome;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services;
    using Client.Common.Services.Navidrome;
    using MugenInjection.Attributes;
    using Subsonic8.AudioPlayback;
    using Subsonic8.Playback;
    using Subsonic8.Framework.ViewModel;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;

    public interface INavidromePlayerViewModel : IScreen { }
    public sealed class NavidromePlayerViewModel : NavidromePageViewModel, INavidromePlayerViewModel, IHaveState
    {
        private readonly DispatcherTimer _timer;
        private PlaylistItem _observedItem;
        private LyricChoice _selectedLyrics;
        private double _position, _duration;
        private bool _updatingPosition, _followLyrics = true;
        private int _highlightedLine = -1;
        private Song _song;
        private PlaylistItem _resumeItem;
        private long _resumePosition;
        private PlaylistItem _scrubItem;
        private bool _scrubChanged;

        public NavidromePlayerViewModel()
        {
            QueueEntries = new BindableCollection<QueueEntryItem>();
            SelectedItems = new ObservableCollection<object>(); SelectedItems.CollectionChanged += (sender, args) => Changed();
            LyricVariants = new BindableCollection<LyricChoice>(); LyricLines = new BindableCollection<LyricLineItem>();
            Rates = new BindableCollection<NamedOption<double>>();
            foreach (var rate in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 }) Rates.Add(RateOption(rate));
            GainModes = Enum.GetValues(typeof(ReplayGainMode)).Cast<ReplayGainMode>()
                .Select(mode => new NamedOption<ReplayGainMode> { Value = mode, Name = NavidromeText.Get("GainMode" + mode) }).ToList();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (sender, args) => UpdateTiming();
            UpdateDisplayName = () => { };
        }
        [Inject] public IAudioPlayerViewModel Audio { get; set; }
        [Inject] public IPlaylistManagementService PlaylistQueue { get; set; }
        [Inject] public IPlaybackActivityService Activity { get; set; }
        [Inject] public IPlaybackViewModel LegacyPlayback { get; set; }
        public BindableCollection<QueueEntryItem> QueueEntries { get; private set; }
        public ObservableCollection<object> SelectedItems { get; private set; }
        public BindableCollection<LyricChoice> LyricVariants { get; private set; }
        public BindableCollection<LyricLineItem> LyricLines { get; private set; }
        public BindableCollection<NamedOption<double>> Rates { get; private set; }
        public IList<NamedOption<ReplayGainMode>> GainModes { get; private set; }
        public NamedOption<ReplayGainMode> SelectedGainMode
        {
            get { return GainModes.FirstOrDefault(mode => mode.Value == Audio.GainMode); }
            set { if (value != null) Audio.GainMode = value.Value; NotifyOfPropertyChange(); }
        }
        public string PreAmplificationText { get { return string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("PreAmplificationValue"), Audio.PreAmplification); } }
        public PlaylistItem CurrentItem { get { return Audio != null && Audio.CurrentItem != null && PlaylistQueue.Items.Contains(Audio.CurrentItem) ? Audio.CurrentItem : null; } }
        public bool HasCurrentItem { get { return CurrentItem != null; } }
        public bool HasPlaybackError { get { return !string.IsNullOrEmpty(Audio.PlaybackError); } }
        public string BackgroundArtwork { get; private set; }
        public string Artwork { get { return CurrentItem == null ? Client.Common.Services.SubsonicService.CoverArtPlaceholder : CurrentItem.OriginalCoverArtUrl; } }
        public string Title { get { return CurrentItem == null ? NavidromeText.Get("NoCurrentSong") : CurrentItem.Title; } }
        public string Artist { get { return CurrentItem == null ? null : CurrentItem.Artist; } }
        public string Album { get { return _song == null ? null : _song.Album; } }
        public string RatingText { get { return CurrentItem == null ? null : string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("RatingValue"), CurrentItem.UserRating); } }
        public bool CanPlayPause { get { return PlaylistQueue.HasElements; } }
        public bool IsPlaying { get { return PlaylistQueue.IsPlaying; } }
        public bool CanOpenArtist { get { return CurrentAccount && !string.IsNullOrEmpty(CurrentItem.ArtistId); } }
        public void OpenArtist() { if (CanOpenArtist) NavigationService.NavigateToViewModel<NavidromeDetailViewModel>(NavidromeRoute.For("artist", CurrentItem.ArtistId)); }
        public bool CanPrevious { get { return PlaylistQueue.HasElements; } }
        public bool CanNext { get { return PlaylistQueue.HasElements; } }
        public string PlayPauseGlyph { get { return PlaylistQueue.IsPlaying ? "\uE103" : "\uE102"; } }
        public string PlayPauseLabel { get { return NavidromeText.Get(PlaylistQueue.IsPlaying ? "Pause" : "Play"); } }
        public string ShuffleLabel { get { return MixRadioText.Get(PlaylistQueue.ShuffleOn ? "ShuffleOn" : "ShuffleOff"); } }
        public string RepeatLabel { get { return MixRadioText.Get("Repeat" + PlaylistQueue.RepeatMode); } }
        public bool CanShuffle { get { return PlaylistQueue.Items.Count > 1; } }
        public bool CanRepeat { get { return PlaylistQueue.HasElements; } }
        public bool CanStop { get { return PlaylistQueue.IsPlaying || PlaylistQueue.IsPaused; } }
        public bool CanClearQueue { get { return PlaylistQueue.HasElements; } }
        private bool CurrentAccount { get { return CurrentItem != null && CurrentItem.AccountKey == Session.AccountKey && !string.IsNullOrEmpty(CurrentItem.SongId); } }
        public bool CanFavourite { get { return !IsWorking && CurrentAccount; } }
        public string FavouriteLabel { get { return NavidromeText.Get(CurrentItem != null && CurrentItem.IsStarred ? "Unfavourite" : "Favourite"); } }
        public bool CanRate { get { return CanFavourite; } }
        public bool CanBookmark { get { return CanFavourite && PlaylistQueue.CurrentItem != null; } }
        public bool CanDetails { get { return CurrentAccount; } }
        public bool CanDownload { get { return !IsWorking && CurrentAccount && Session.User != null && Session.User.DownloadRole; } }
        public bool CanSaveQueue { get { return !IsWorking && PlaylistQueue.Items.All(item => item.AccountKey == Session.AccountKey && !string.IsNullOrEmpty(item.SongId)); } }
        public bool CanRestoreQueue { get { return !IsWorking; } }
        public bool CanSyncListening { get { return !IsWorking && (Activity.PendingCount > 0 || !string.IsNullOrEmpty(Activity.Error)); } }
        public string PendingListeningText { get { return string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("PendingListening"), Activity.PendingCount); } }
        public bool CanSaveAsPlaylist { get { return !IsWorking && PlaylistQueue.HasElements && CanSaveQueue; } }
        public bool CanRemove { get { return SelectedItems.Count > 0; } }
        public bool CanMoveUp { get { return SelectedItems.Count == 1 && PlaylistQueue.Items.IndexOf(((QueueEntryItem)SelectedItems[0]).Item) > 0; } }
        public bool CanMoveDown { get { return SelectedItems.Count == 1 && PlaylistQueue.Items.IndexOf(((QueueEntryItem)SelectedItems[0]).Item) < PlaylistQueue.Items.Count - 1; } }
        public bool CanReloadLyrics { get { return !IsWorking && CurrentAccount; } }
        public bool HasLyrics { get { return LyricLines.Count > 0; } }
        public string LyricsStatus { get; private set; }
        public string LyricsError { get; private set; }
        public bool IsScrubbing { get; private set; }
        public void BeginScrub()
        {
            if (IsScrubbing || Audio == null || !Audio.CanSeek || CurrentItem == null) return;
            _scrubItem = CurrentItem;
            _scrubChanged = false;
            IsScrubbing = true;
        }
        public void FinishScrub(bool commit)
        {
            if (!IsScrubbing) return;
            var position = _position;
            var shouldSeek = commit && _scrubChanged && ReferenceEquals(_scrubItem, CurrentItem) && Audio.CanSeek;
            IsScrubbing = false;
            _scrubItem = null;
            _scrubChanged = false;
            if (shouldSeek) Audio.Seek(TimeSpan.FromSeconds(position));
            UpdateTiming();
        }
        public bool FollowLyrics { get { return _followLyrics; } set { _followLyrics = value; NotifyOfPropertyChange(); NotifyOfPropertyChange(() => HighlightedLine); } }
        public int HighlightedLine { get { return _highlightedLine; } }
        public string PositionText { get { return NavidromeItem.FormatTime(_position); } }
        public string DurationText { get { return NavidromeItem.FormatTime(_duration); } }
        public double DurationSeconds { get { return _duration; } }
        public double PositionSeconds
        {
            get { return _position; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return;
                var position = Math.Max(0, Math.Min(_duration, value));
                if (position == _position) return;
                _position = position;
                if (!_updatingPosition)
                {
                    if (IsScrubbing) _scrubChanged = true;
                    else if (Audio != null && Audio.CanSeek) Audio.Seek(TimeSpan.FromSeconds(_position));
                }
                NotifyOfPropertyChange(); NotifyOfPropertyChange(() => PositionText);
            }
        }
        public NamedOption<double> SelectedRate
        {
            get { return Rates.FirstOrDefault(rate => Math.Abs(rate.Value - Audio.PlaybackRate) < 0.001); }
            set { if (value != null) Audio.PlaybackRate = value.Value; NotifyOfPropertyChange(); }
        }
        public LyricChoice SelectedLyrics
        {
            get { return _selectedLyrics; }
            set
            {
                _selectedLyrics = value; LyricLines.Clear(); _highlightedLine = -1;
                if (value != null) LyricLines.AddRange(value.Variant.Lines.Select(line => new LyricLineItem { Text = line.Text }));
                NotifyOfPropertyChange(); NotifyOfPropertyChange(() => HasLyrics); UpdateTiming();
            }
        }
        private static NamedOption<double> RateOption(double rate) { return new NamedOption<double> { Value = rate, Name = rate.ToString("0.##", CultureInfo.CurrentCulture) + "×" }; }

        protected override void OnActivate()
        {
            Audio.PropertyChanged += AudioChanged; Audio.PlaybackSeeked += Seeked;
            PlaylistQueue.PropertyChanged += QueueChanged; PlaylistQueue.Items.CollectionChanged += QueueItemsChanged;
            Session.PropertyChanged += SessionChanged; Activity.PropertyChanged += ActivityChanged;
            RebuildQueue(); ApplyResumePosition(); _timer.Start(); base.OnActivate();
        }
        protected override void OnDeactivate(bool close)
        {
            FinishScrub(false);
            _timer.Stop(); Audio.PropertyChanged -= AudioChanged; Audio.PlaybackSeeked -= Seeked;
            PlaylistQueue.PropertyChanged -= QueueChanged; PlaylistQueue.Items.CollectionChanged -= QueueItemsChanged;
            Session.PropertyChanged -= SessionChanged; Activity.PropertyChanged -= ActivityChanged;
            if (_observedItem != null) _observedItem.PropertyChanged -= CurrentChanged;
            _observedItem = null; base.OnDeactivate(close);
        }
        private async void AudioChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == "CurrentItem" || args.PropertyName == "CanSeek" && !Audio.CanSeek) FinishScrub(false);
            if (args.PropertyName == "CurrentItem") ApplyResumePosition();
            if (args.PropertyName == "GainMode") NotifyOfPropertyChange(() => SelectedGainMode);
            if (args.PropertyName == "PreAmplification") NotifyOfPropertyChange(() => PreAmplificationText);
            Changed();
            if (args.PropertyName == "CurrentItem" && PageActive) await Reload();
            if (args.PropertyName == "PlaybackRate")
            {
                if (SelectedRate == null) Rates.Add(RateOption(Audio.PlaybackRate));
                NotifyOfPropertyChange(() => SelectedRate);
            }
            UpdateTiming();
        }
        private void Seeked(object sender, EventArgs args) { UpdateTiming(); }
        private void QueueChanged(object sender, PropertyChangedEventArgs args) { foreach (var row in QueueEntries) row.IsCurrent = ReferenceEquals(row.Item, PlaylistQueue.CurrentItem); Changed(); }
        private async void QueueItemsChanged(object sender, NotifyCollectionChangedEventArgs args)
        { RebuildQueue(); Changed(); if (PageActive && !ReferenceEquals(CurrentItem, _observedItem)) await Reload(); }
        private void SessionChanged(object sender, PropertyChangedEventArgs args) { Changed(); }
        private void ActivityChanged(object sender, PropertyChangedEventArgs args) { NotifyOfPropertyChange(() => PendingListeningText); NotifyOfPropertyChange(() => CanSyncListening); }
        private void CurrentChanged(object sender, PropertyChangedEventArgs args) { Changed(); }
        private void RebuildQueue()
        {
            SelectedItems.Clear(); QueueEntries.Clear();
            QueueEntries.AddRange(PlaylistQueue.Items.Select((item, index) => new QueueEntryItem { Item = item, Index = index, IsCurrent = ReferenceEquals(item, PlaylistQueue.CurrentItem) }));
        }
        protected override void Changed()
        {
            base.Changed();
            foreach (var name in new[] { "CurrentItem", "HasCurrentItem", "HasPlaybackError", "Artwork", "BackgroundArtwork", "Title", "Artist", "Album", "RatingText", "CanPlayPause", "CanPrevious", "CanNext",
                "PlayPauseGlyph", "PlayPauseLabel", "IsPlaying", "CanOpenArtist", "ShuffleLabel", "RepeatLabel", "CanShuffle", "CanRepeat", "CanStop", "CanClearQueue", "CanFavourite", "CanRate", "CanBookmark", "CanDetails", "CanDownload",
                "CanSaveQueue", "CanRestoreQueue", "CanSaveAsPlaylist", "CanRemove", "CanMoveUp", "CanMoveDown", "CanReloadLyrics", "FavouriteLabel", "CanSyncListening", "CanImportQueue", "CanExportQueue" }) NotifyOfPropertyChange(name);
        }
        public override async Task Reload()
        {
            var version = ++LoadVersion; var item = CurrentItem;
            DisplayName = NavidromeText.Get("Player"); IsLoading = true; Error = null; LyricsError = null; LyricsStatus = null;
            _song = null; BackgroundArtwork = null; LyricVariants.Clear(); SelectedLyrics = null;
            if (_observedItem != null) _observedItem.PropertyChanged -= CurrentChanged;
            _observedItem = item; if (item != null) item.PropertyChanged += CurrentChanged;
            try
            {
                if (!CurrentAccount) { LyricsStatus = NavidromeText.Get("NoCurrentLyrics"); return; }
                if (item.Uri != null && item.Uri.Scheme == "ms-appdata")
                {
                    var song = await Offline.GetSongAsync(item); var lyrics = await Offline.GetLyricsAsync(item);
                    if (!Matches(version, item)) return;
                    _song = song; ApplyLyrics(lyrics ?? new List<LyricVariant>(), true); return;
                }
                await Session.ConnectAsync(); var api = Session.CreateApi();
                if (!Matches(version, item)) return;
                var songInfo = await api.GetSong(item.SongId).ReadAsync();
                if (!Matches(version, item)) return;
                _song = songInfo; Changed();
                await Task.WhenAll(LoadArtistBackground(api, item, version), LoadLyrics(api, item, version));
            }
            catch (Exception exception) { if (Matches(version, item)) LyricsError = exception.Message; }
            finally { if (version == LoadVersion) { IsLoading = false; NotifyLyrics(); Changed(); } }
        }
        private async Task LoadArtistBackground(NavidromeApi api, PlaylistItem item, int version)
        {
            if (string.IsNullOrEmpty(item.ArtistId)) return;
            try
            {
                var artist = await api.GetArtistInfo(item.ArtistId).ReadAsync();
                if (Matches(version, item)) { BackgroundArtwork = artist.ImageUrl; NotifyOfPropertyChange(() => BackgroundArtwork); }
            }
            catch (Exception) { /* Artist imagery is optional; retain the original black backdrop when unavailable. */ }
        }
        private async Task LoadLyrics(NavidromeApi api, PlaylistItem item, int version)
        {
            var variants = await (Session.Capabilities.Supports("songLyrics") ? api.GetLyrics(item.SongId, true) : api.GetLegacyLyrics(item.Artist, item.Title)).ReadAsync();
            if (Matches(version, item)) ApplyLyrics(variants, false);
        }
        private bool Matches(int version, PlaylistItem item) { return item != null && PageActive && version == LoadVersion && ReferenceEquals(item, CurrentItem) && item.AccountKey == Session.AccountKey; }
        private void ApplyLyrics(IList<LyricVariant> variants, bool offline)
        {
            LyricVariants.Clear(); LyricVariants.AddRange(variants.Select(variant => new LyricChoice { Variant = variant }));
            SelectedLyrics = LyricVariants.FirstOrDefault();
            LyricsStatus = variants.Count == 0 ? NavidromeText.Get(offline ? "NoOfflineLyrics" : "NoLyrics") : null; NotifyLyrics();
        }
        private void NotifyLyrics() { NotifyOfPropertyChange(() => LyricsStatus); NotifyOfPropertyChange(() => LyricsError); NotifyOfPropertyChange(() => HasLyrics); }
        public void UpdateTiming()
        {
            if (Audio == null) return;
            if (IsScrubbing && (!ReferenceEquals(_scrubItem, CurrentItem) || !Audio.CanSeek))
            { IsScrubbing = false; _scrubItem = null; _scrubChanged = false; }
            var position = Math.Max(0, Audio.GetCurrentPosition().TotalSeconds);
            _updatingPosition = true;
            try
            {
                var duration = HasCurrentItem ? Math.Max(0, Audio.GetDuration().TotalSeconds) : 0;
                if (duration != _duration) { _duration = duration; NotifyOfPropertyChange(() => DurationSeconds); NotifyOfPropertyChange(() => DurationText); }
                if (!IsScrubbing) PositionSeconds = position;
            }
            finally { _updatingPosition = false; }
            var index = Audio.CanSeek && SelectedLyrics != null ? SelectedLyrics.Variant.LineAt((long)(position * 1000)) : -1;
            if (index == _highlightedLine) return;
            if (_highlightedLine >= 0 && _highlightedLine < LyricLines.Count) LyricLines[_highlightedLine].IsCurrent = false;
            _highlightedLine = index;
            if (index >= 0 && index < LyricLines.Count) LyricLines[index].IsCurrent = true;
            NotifyOfPropertyChange(() => HighlightedLine);
        }
        public void PlayPause() { PlaylistQueue.Handle(new PlayPauseMessage()); }
        public void Previous() { PlaylistQueue.Handle(new JumpToPreviousMessage()); }
        public void Next() { PlaylistQueue.Handle(new JumpToNextMessage()); }
        public void Shuffle() { PlaylistQueue.Handle(new ToggleShuffleMessage()); }
        public void Repeat() { PlaylistQueue.Handle(new ToggleRepeatMessage()); }
        public void Stop() { PlaylistQueue.Handle(new StopMessage()); }
        public void ClearQueue() { PlaylistQueue.Clear(); LyricVariants.Clear(); SelectedLyrics = null; Changed(); }
        public void PlayEntry(ItemClickEventArgs args) { var item = (QueueEntryItem)args.ClickedItem; PlaylistQueue.Handle(new PlayItemAtIndexMessage(PlaylistQueue.Items.IndexOf(item.Item))); }
        public void Remove() { PlaylistQueue.Handle(new RemoveItemsMessage { Queue = SelectedItems.Cast<QueueEntryItem>().Select(row => row.Item).ToList() }); }
        public void MoveUp() { Move(-1); }
        public void MoveDown() { Move(1); }
        private void Move(int delta)
        {
            var row = SelectedItems.Cast<QueueEntryItem>().Single(); var target = PlaylistQueue.Items.IndexOf(row.Item) + delta;
            if (target < 0 || target >= PlaylistQueue.Items.Count) return;
            PlaylistQueue.MoveItem(row.Item, target); SelectedItems.Add(QueueEntries.Single(entry => ReferenceEquals(entry.Item, row.Item)));
        }
        public Task SaveQueue() { return RunOperation(api => MediaQueue.SaveServerQueueAsync()); }
        public Task RestoreQueue() { return RunOperation(api => MediaQueue.RestoreServerQueueAsync()); }
        public bool CanImportQueue { get { return !IsWorking; } }
        public bool CanExportQueue { get { return !IsWorking && PlaylistQueue.HasElements; } }
        public Task ImportQueue() { return RunLocalOperation(() => LegacyPlayback.LoadPlaylist()); }
        public Task ExportQueue() { return RunLocalOperation(() => LegacyPlayback.SavePlaylist()); }
        public Task SyncListening() { return RunOperation(api => Activity.SyncAsync(true)); }
        public void Downloads() { NavigationService.NavigateToViewModel<NavidromeDownloadsViewModel>(); }
        public void Account() { NavigationService.NavigateToViewModel<NavidromeAccountViewModel>(); }
        public void Details() { if (CurrentAccount) NavigationService.NavigateToViewModel<NavidromeDetailViewModel>(NavidromeRoute.For("song", CurrentItem.SongId)); }
        public Task Download() { var item = CurrentItem; return RunOperation(async api => { if (item.AccountKey != api.AccountKey) throw new OperationCanceledException(); var song = await api.GetSong(item.SongId).ReadAsync(); RequireAccount(api); await Offline.EnqueueAsync(api, new[] { song }); Downloads(); }); }
        public Task Favourite()
        {
            var item = CurrentItem; var value = !item.IsStarred;
            return RunOperation(async api =>
            {
                if (item.AccountKey != api.AccountKey) throw new OperationCanceledException();
                await api.SetStarred(new Song { Id = item.SongId }, value).ReadAsync(); RequireAccount(api);
                foreach (var queued in PlaylistQueue.Items.Where(queued => queued.AccountKey == api.AccountKey && queued.SongId == item.SongId)) queued.IsStarred = value;
                Changed();
            });
        }
        public Task Rate(FrameworkElement anchor)
        {
            var item = CurrentItem;
            return RunOperation(async api =>
            {
                var rating = new FormField { LabelKey = "Rating", Kind = FormFieldKind.Choice, Value = item.UserRating, Choices = Enumerable.Range(0, 6).Cast<object>().ToList() };
                if (!await Dialogs.FormAsync(anchor, "Rate", new[] { rating })) return;
                if (item.AccountKey != api.AccountKey) throw new OperationCanceledException(); RequireAccount(api);
                await api.SetRating(item.SongId, (int)rating.Value).ReadAsync(); RequireAccount(api);
                foreach (var queued in PlaylistQueue.Items.Where(queued => queued.AccountKey == api.AccountKey && queued.SongId == item.SongId)) queued.UserRating = (int)rating.Value;
                Changed();
            });
        }
        public Task Bookmark(FrameworkElement anchor)
        {
            var item = CurrentItem; var position = Math.Max(0, (long)Audio.GetCurrentPosition().TotalMilliseconds);
            return RunOperation(async api =>
            {
                var comment = new FormField { LabelKey = "Comment" };
                if (!await Dialogs.FormAsync(anchor, "SaveBookmark", new[] { comment })) return;
                if (item.AccountKey != api.AccountKey) throw new OperationCanceledException(); RequireAccount(api);
                await api.SaveBookmark(item.SongId, position, comment.Value as string).ReadAsync();
            });
        }
        public Task SaveAsPlaylist(FrameworkElement anchor)
        {
            var queue = PlaylistQueue.Items.ToList();
            return RunOperation(async api =>
            {
                var name = new FormField { LabelKey = "Name" };
                if (!await Dialogs.FormAsync(anchor, "CreatePlaylist", new[] { name }, () => string.IsNullOrWhiteSpace(name.Value as string) ? NavidromeText.Get("NameRequired") : null)) return;
                if (queue.Any(item => item.AccountKey != api.AccountKey || string.IsNullOrEmpty(item.SongId))) throw new InvalidOperationException(NavidromeText.Get("QueueContainsOtherMedia"));
                RequireAccount(api); var playlist = await api.SavePlaylist(name.Value as string, queue.Select(item => item.SongId)).ReadAsync();
                RequireAccount(api); NavigationService.NavigateToViewModel<NavidromeDetailViewModel>(NavidromeRoute.For("playlist", playlist.Id));
            });
        }
        public Task ReloadLyrics()
        {
            var item = CurrentItem; var version = LoadVersion;
            return RunOperation(async api =>
            {
                if (item.AccountKey != api.AccountKey) throw new OperationCanceledException();
                var variants = await (Session.Capabilities.Supports("songLyrics") ? api.GetLyrics(item.SongId, true) : api.GetLegacyLyrics(item.Artist, item.Title)).ReadAsync();
                if (Matches(version, item)) { LyricsError = null; ApplyLyrics(variants, false); }
            });
        }

        private void ApplyResumePosition()
        {
            if (_resumeItem == null) return;
            if (_resumeItem.AccountKey != Session.AccountKey || !ReferenceEquals(PlaylistQueue.CurrentItem, _resumeItem))
            { _resumeItem = null; return; }
            if (!ReferenceEquals(Audio.CurrentItem, _resumeItem)) return;
            Audio.RestorePosition(_resumePosition);
            _resumeItem = null;
        }
        public void SaveState(Dictionary<string, object> state, List<Type> knownTypes)
        {
            LegacyPlayback.SaveState(state, knownTypes);
            state["QueueAccount"] = PlaylistQueue.Items.All(item => item.AccountKey == Session.AccountKey) ? Session.AccountKey : string.Empty;
            state["PositionMilliseconds"] = _resumeItem != null && ReferenceEquals(PlaylistQueue.CurrentItem, _resumeItem)
                ? Math.Max(0, _resumePosition) : ReferenceEquals(PlaylistQueue.CurrentItem, Audio.CurrentItem)
                    ? Math.Max(0, (long)Audio.GetCurrentPosition().TotalMilliseconds) : 0L;
            state[nameof(FollowLyrics)] = FollowLyrics;
        }
        public void LoadState(string parameter, Dictionary<string, object> state)
        {
            object value;
            if (state.TryGetValue(nameof(FollowLyrics), out value) && value is bool) FollowLyrics = (bool)value;
            if (PlaylistQueue.HasElements || !state.TryGetValue("QueueAccount", out value) || (value as string) != Session.AccountKey) return;
            LegacyPlayback.LoadState(parameter, state);
            if (state.TryGetValue("PositionMilliseconds", out value) && value is long)
            { _resumePosition = (long)value; _resumeItem = PlaylistQueue.CurrentItem; ApplyResumePosition(); }
        }
    }
}
