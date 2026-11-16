namespace Mixdrome.Phone.Views
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Mixdrome.Phone.Common;
    using Mixdrome.Phone.Services;
    using Windows.Foundation.Collections;
    using Windows.Media.Playback;
    using Windows.Phone.UI.Input;
    using Windows.Storage;
    using Windows.UI.Core;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Automation;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Controls.Primitives;
    using Windows.UI.Xaml.Input;
    using Windows.UI.Xaml.Media.Imaging;
    using Windows.UI.Xaml.Navigation;

    public sealed class PlaybackNavigationParameter
    {
        public List<SongDisplayItem> Playlist { get; set; }
        public int StartIndex { get; set; }
        public bool? Shuffle { get; set; }
    }

    public sealed partial class PlaybackPage : Page
    {
        private readonly DispatcherTimer progressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private TaskCompletionSource<bool> ready;
        private CancellationTokenSource activation;
        private bool active;
        private bool updatingProgress;
        private bool seeking;
        private uint seekPointer;
        private bool shuffle;
        private int repeat;
        private int currentIndex = -1;
        private string queueVersion = string.Empty;
        private string latestVersion = string.Empty;
        private MediaPlayer player;
        private readonly AudioStateVersionGate stateGate = new AudioStateVersionGate();
        private bool canNext;
        private bool playbackFailed;
        private TrackInfo displayedTrack;
        public ObservableCollection<TrackInfo> QueueItems { get; private set; } = new ObservableCollection<TrackInfo>();

        public PlaybackPage()
        {
            InitializeComponent();
            DataContext = this;
            progressTimer.Tick += OnProgressTimerTick;
            ProgressSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnSeekPressed), true);
            ProgressSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnSeekReleased), true);
            ProgressSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnSeekReleased), true);
            ResetDisplay();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs args)
        {
            base.OnNavigatedTo(args);
            active = true;
            activation = new CancellationTokenSource();
            var token = activation.Token;
            ready = new TaskCompletionSource<bool>();
            HardwareButtons.BackPressed += OnBackPressed;
            // Subscribe before creating the player, which may start a cold audio task.
            BackgroundMediaPlayer.MessageReceivedFromBackground += OnMessage;
            try
            {
                player = BackgroundMediaPlayer.Current;
                player.CurrentStateChanged += OnMediaStateChanged;
                Send(BackgroundAudioMessages.RequestState, true);
                var timeout = Task.Delay(TimeSpan.FromSeconds(10), token);
                if (await Task.WhenAny(ready.Task, timeout) != ready.Task)
                {
                    token.ThrowIfCancellationRequested();
                    PlaybackStatusText.Text = PhoneText.Get("PlaybackUnavailable");
                    return;
                }
                token.ThrowIfCancellationRequested();
                // Returning from another page reconnects; it never replays the navigation queue.
                if (args.NavigationMode != NavigationMode.Back)
                {
                    var parameter = args.Parameter as PlaybackNavigationParameter;
                    if (parameter != null) await StartQueueAsync(parameter, token);
                    else
                    {
                        var id = args.Parameter as string;
                        if (!string.IsNullOrEmpty(id))
                            await StartQueueAsync(new PlaybackNavigationParameter
                            {
                                Playlist = new List<SongDisplayItem> { new SongDisplayItem { Id = id } }, StartIndex = 0
                            }, token);
                    }
                }
                token.ThrowIfCancellationRequested();
                UpdatePlaybackState();
                progressTimer.Start();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Debug.WriteLine("Phone playback failed: " + exception.GetType().Name);
                if (active) PlaybackStatusText.Text = PhoneText.Get("PlaybackFailed");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs args)
        {
            active = false;
            seeking = false;
            if (activation != null) { activation.Cancel(); activation.Dispose(); activation = null; }
            HardwareButtons.BackPressed -= OnBackPressed;
            BackgroundMediaPlayer.MessageReceivedFromBackground -= OnMessage;
            if (player != null) player.CurrentStateChanged -= OnMediaStateChanged;
            progressTimer.Stop();
            base.OnNavigatedFrom(args);
        }

        private void OnBackPressed(object sender, BackPressedEventArgs args)
        {
            if (!args.Handled && Frame.CanGoBack) { args.Handled = true; Frame.GoBack(); }
        }

        private async Task StartQueueAsync(PlaybackNavigationParameter parameter, CancellationToken token)
        {
            if (parameter.Playlist == null || parameter.Playlist.Count == 0
                || parameter.StartIndex < 0 || parameter.StartIndex >= parameter.Playlist.Count)
            {
                PlaybackStatusText.Text = PhoneText.Get("PlaybackEmpty");
                return;
            }
            var service = await PhoneSubsonicServiceFactory.CreatePlaybackServiceAsync();
            token.ThrowIfCancellationRequested();
            var tracks = new List<TrackInfo>();
            foreach (var song in parameter.Playlist)
            {
                if (song == null) throw new InvalidOperationException("Missing queue item.");
                tracks.Add(new TrackInfo
                {
                    Id = song.Id,
                    Title = string.IsNullOrEmpty(song.Title) ? PhoneText.Get("UnknownTrack") : song.Title,
                    Artist = string.IsNullOrEmpty(song.Artist) ? PhoneText.Get("UnknownArtist") : song.Artist,
                    Album = song.Album,
                    Duration = song.Duration,
                    CoverArt = ResolveArtwork(song.CoverArt, service),
                    StreamUrl = string.IsNullOrEmpty(song.StreamUrl) ? service.GetStreamUri(song.Id).ToString() : song.StreamUrl,
                    // Explicit external streams are radio, not catalog song IDs.
                    Reporting = string.IsNullOrEmpty(song.StreamUrl) ? service.CreateReportingProfile() : null
                });
            }
            var validation = new AudioQueueState();
            if (!validation.Replace(tracks, parameter.StartIndex)) throw new InvalidOperationException("Invalid playback queue.");
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                BackgroundAudioMessages.TransferFilePrefix + Guid.NewGuid().ToString("N") + ".json", CreationCollisionOption.FailIfExists);
            var sent = false;
            try
            {
                await FileIO.WriteTextAsync(file, AudioJson.Serialize(tracks));
                token.ThrowIfCancellationRequested();
                var command = new ValueSet
                {
                    { BackgroundAudioMessages.ReplaceQueue, true },
                    { BackgroundAudioMessages.QueueFile, file.Name },
                    { BackgroundAudioMessages.CurrentIndex, parameter.StartIndex }
                };
                if (parameter.Shuffle.HasValue) command.Add(BackgroundAudioMessages.Shuffle, parameter.Shuffle.Value);
                BackgroundMediaPlayer.SendMessageToBackground(command);
                sent = true;
            }
            finally { if (!sent) await file.DeleteAsync(); }
        }

        private static string ResolveArtwork(string artwork, WP8SubsonicService service)
        {
            Uri uri;
            if (string.IsNullOrEmpty(artwork)) return "ms-appx:///Assets/MixRadio/DefaultArtwork300.png";
            return Uri.TryCreate(artwork, UriKind.Absolute, out uri) ? uri.ToString() : service.GetCoverArtUrl(artwork);
        }

        private async void OnMessage(object sender, MediaPlayerDataReceivedEventArgs args)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    if (!active) return;
                    try { await ApplyStateAsync(args.Data); }
                    catch (Exception exception)
                    {
                        Debug.WriteLine("Phone audio state failed: " + exception.GetType().Name);
                        if (active) PlaybackStatusText.Text = PhoneText.Get("PlaybackFailed");
                    }
                });
            }
            catch (Exception exception) { Debug.WriteLine("Phone audio dispatcher unavailable: " + exception.GetType().Name); }
        }

        private async Task ApplyStateAsync(ValueSet data)
        {
            var stateSession = data.ContainsKey(BackgroundAudioMessages.Session) ? data[BackgroundAudioMessages.Session] as string : string.Empty;
            var stateSequence = data.ContainsKey(BackgroundAudioMessages.Sequence) && data[BackgroundAudioMessages.Sequence] is int
                ? (int)data[BackgroundAudioMessages.Sequence] : -1;
            if (!stateGate.Accept(stateSession, stateSequence)) return;
            if (data.ContainsKey(BackgroundAudioMessages.Ready) || data.ContainsKey(BackgroundAudioMessages.Snapshot))
                ready.TrySetResult(true);
            if (data.ContainsKey(BackgroundAudioMessages.Failure))
            {
                playbackFailed = true;
                PlaybackStatusText.Text = PhoneText.Get("PlaybackFailed");
                return;
            }
            var stateVersion = data.ContainsKey(BackgroundAudioMessages.QueueVersion)
                ? data[BackgroundAudioMessages.QueueVersion] as string ?? string.Empty : string.Empty;
            var replaced = stateVersion != queueVersion;
            latestVersion = stateVersion;
            var currentActivation = activation;
            if (!string.IsNullOrEmpty(stateVersion) && stateVersion != queueVersion)
            {
                var file = await ApplicationData.Current.LocalFolder.GetFileAsync(BackgroundAudioMessages.SessionFile);
                var snapshot = AudioJson.Deserialize<AudioQueueSnapshot>(await FileIO.ReadTextAsync(file));
                if (!active || activation != currentActivation || latestVersion != stateVersion || !stateGate.IsCurrent(stateSession, stateSequence)) return;
                // The file is replaced atomically. Reject a newer queue read for an older message.
                if (snapshot.Version != stateVersion) { Send(BackgroundAudioMessages.RequestState, true); return; }
                QueueItems.Clear();
                foreach (var track in snapshot.Tracks) QueueItems.Add(track);
                queueVersion = stateVersion;
            }
            if (!active || activation != currentActivation || latestVersion != stateVersion || !stateGate.IsCurrent(stateSession, stateSequence)) return;
            if (string.IsNullOrEmpty(stateVersion)) { QueueItems.Clear(); queueVersion = string.Empty; }
            if (data.ContainsKey(BackgroundAudioMessages.TrackChanged) || replaced) playbackFailed = false;
            canNext = data.ContainsKey(BackgroundAudioMessages.CanNext) && data[BackgroundAudioMessages.CanNext] is bool
                && (bool)data[BackgroundAudioMessages.CanNext];
            currentIndex = data.ContainsKey(BackgroundAudioMessages.CurrentIndex) && data[BackgroundAudioMessages.CurrentIndex] is int
                ? (int)data[BackgroundAudioMessages.CurrentIndex] : -1;
            shuffle = data.ContainsKey(BackgroundAudioMessages.Shuffle) && data[BackgroundAudioMessages.Shuffle] is bool
                && (bool)data[BackgroundAudioMessages.Shuffle];
            repeat = data.ContainsKey(BackgroundAudioMessages.Repeat) && data[BackgroundAudioMessages.Repeat] is int
                ? (int)data[BackgroundAudioMessages.Repeat] : 0;
            if (currentIndex >= 0 && currentIndex < QueueItems.Count)
            {
                QueueListView.SelectedIndex = currentIndex;
                ShowTrack(QueueItems[currentIndex]);
            }
            else ResetDisplay();
            UpdatePlaybackState();
        }

        private void ShowTrack(TrackInfo track)
        {
            if (ReferenceEquals(displayedTrack, track)) return;
            displayedTrack = track;
            SongTitleText.Text = AboutTitleText.Text = track.Title ?? PhoneText.Get("UnknownTrack");
            ArtistText.Text = AboutArtistText.Text = track.Artist ?? PhoneText.Get("UnknownArtist");
            AboutAlbumText.Text = string.IsNullOrEmpty(track.Album) ? PhoneText.Get("UnknownAlbum") : track.Album;
            var image = new BitmapImage(new Uri(string.IsNullOrEmpty(track.CoverArt)
                ? "ms-appx:///Assets/MixRadio/DefaultArtwork300.png" : track.CoverArt, UriKind.Absolute));
            AlbumArtImage.Source = image;
            BackgroundImage.Source = image;
        }

        private void ResetDisplay()
        {
            displayedTrack = null;
            SongTitleText.Text = PhoneText.Get("PlaybackEmpty");
            ArtistText.Text = AboutTitleText.Text = AboutArtistText.Text = AboutAlbumText.Text = string.Empty;
            PlaybackStatusText.Text = string.Empty;
            PreviousButton.IsEnabled = PlayPauseButton.IsEnabled = NextButton.IsEnabled = false;
            ShuffleButton.IsEnabled = RepeatButton.IsEnabled = false;
        }

        private async void OnMediaStateChanged(MediaPlayer sender, object args)
        {
            try { await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { if (active) UpdatePlaybackState(); }); }
            catch (Exception exception) { Debug.WriteLine("Phone audio dispatcher unavailable: " + exception.GetType().Name); }
        }

        private void UpdatePlaybackState()
        {
            if (player == null) return;
            var hasTrack = currentIndex >= 0 && currentIndex < QueueItems.Count;
            var state = player.CurrentState;
            var playing = state == MediaPlayerState.Playing;
            PlayPauseGlyph.Text = playing ? "\uE103" : "\uE102";
            AutomationProperties.SetName(PlayPauseButton, PhoneText.Get(playing ? "Pause" : "Play"));
            PreviousButton.IsEnabled = PlayPauseButton.IsEnabled = hasTrack;
            NextButton.IsEnabled = hasTrack && canNext;
            ShuffleButton.IsEnabled = RepeatButton.IsEnabled = hasTrack;
            ShuffleButton.Foreground = shuffle ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["MixRadioAccentBrush"]
                : (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
            RepeatButton.Foreground = repeat != 0 ? (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["MixRadioAccentBrush"]
                : (Windows.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationForegroundThemeBrush"];
            RepeatOneBadge.Visibility = repeat == 2 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(RepeatButton, PhoneText.Get(repeat == 0 ? "RepeatOff" : repeat == 1 ? "RepeatAll" : "RepeatOne"));
            PlaybackStatusText.Text = playbackFailed ? PhoneText.Get("PlaybackFailed")
                : state == MediaPlayerState.Opening || state == MediaPlayerState.Buffering ? PhoneText.Get("PlaybackLoading") : string.Empty;
            UpdateProgress();
        }

        private void OnProgressTimerTick(object sender, object args) { if (active) UpdateProgress(); }

        private void UpdateProgress()
        {
            if (player == null) return;
            var duration = player.NaturalDuration.TotalSeconds;
            var position = player.Position.TotalSeconds;
            updatingProgress = true;
            try
            {
                ProgressSlider.IsEnabled = player.CanSeek && duration > 0;
                if (!seeking)
                {
                    ProgressSlider.Maximum = duration > 0 ? duration : 1;
                    ProgressSlider.Value = Math.Max(0, Math.Min(position, ProgressSlider.Maximum));
                }
                CurrentTimeText.Text = FormatTime(position);
                TotalTimeText.Text = FormatTime(duration);
            }
            finally { updatingProgress = false; }
        }

        private static string FormatTime(double seconds)
        {
            if (double.IsInfinity(seconds) || double.IsNaN(seconds) || seconds < 0) seconds = 0;
            var time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1 ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0}:{1:00}:{2:00}", (int)time.TotalHours, time.Minutes, time.Seconds)
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0}:{1:00}", (int)time.TotalMinutes, time.Seconds);
        }

        private void OnProgressSliderChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            if (active && !updatingProgress && !seeking && player != null && player.CanSeek) Send(BackgroundAudioMessages.Seek, args.NewValue);
        }
        private void OnSeekPressed(object sender, PointerRoutedEventArgs args)
        {
            if (ProgressSlider.IsEnabled) { seeking = true; seekPointer = args.Pointer.PointerId; }
        }
        private void OnSeekReleased(object sender, PointerRoutedEventArgs args)
        {
            if (!seeking || args.Pointer.PointerId != seekPointer) return;
            seeking = false;
            if (player != null && player.CanSeek) Send(BackgroundAudioMessages.Seek, ProgressSlider.Value);
        }
        private void OnPlayPauseClick(object sender, RoutedEventArgs args) { Send(player.CurrentState == MediaPlayerState.Playing ? BackgroundAudioMessages.Pause : BackgroundAudioMessages.Play, true); }
        private void OnPreviousClick(object sender, RoutedEventArgs args) { Send(BackgroundAudioMessages.Previous, true); }
        private void OnNextClick(object sender, RoutedEventArgs args) { Send(BackgroundAudioMessages.Next, true); }
        private void OnShuffleClick(object sender, RoutedEventArgs args) { Send(BackgroundAudioMessages.Shuffle, !shuffle); }
        private void OnRepeatClick(object sender, RoutedEventArgs args) { Send(BackgroundAudioMessages.Repeat, (repeat + 1) % 3); }
        private void OnQueueItemClick(object sender, ItemClickEventArgs args)
        {
            var index = QueueItems.IndexOf(args.ClickedItem as TrackInfo);
            if (index >= 0) Send(BackgroundAudioMessages.SelectTrack, index);
        }
        private void OnArtworkSizeChanged(object sender, SizeChangedEventArgs args) { AlbumArtBorder.Height = args.NewSize.Width; }

        private void Send(string key, object value)
        {
            if (!active) return;
            try { BackgroundMediaPlayer.SendMessageToBackground(new ValueSet { { key, value } }); }
            catch (Exception exception)
            {
                Debug.WriteLine("Phone audio command failed: " + exception.GetType().Name);
                PlaybackStatusText.Text = PhoneText.Get("PlaybackUnavailable");
            }
        }
    }
}
