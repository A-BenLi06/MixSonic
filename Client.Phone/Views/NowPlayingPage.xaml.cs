namespace Mixdrome.Phone.Views
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Threading.Tasks;
    using Client.Common.Models.Subsonic;
    using Mixdrome.Phone.Services;
    using Windows.Phone.UI.Input;
    using Windows.UI.Popups;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media.Imaging;
    using Windows.UI.Xaml.Navigation;

    public sealed partial class NowPlayingPage : Page
    {
        private WP8SubsonicService _subsonicService;
        private ProgressRing _loadingIndicator;
        private TextBlock _emptyStateText;
        private TextBlock _entryCountText;
        private Image _backgroundImage;

        public ObservableCollection<NowPlayingDisplayItem> Entries { get; private set; }

        public NowPlayingPage()
        {
            Entries = new ObservableCollection<NowPlayingDisplayItem>();
            InitializeComponent();
            DataContext = this;
            _loadingIndicator = FindName("LoadingIndicator") as ProgressRing;
            _emptyStateText = FindName("EmptyStateText") as TextBlock;
            _entryCountText = FindName("EntryCountText") as TextBlock;
            _backgroundImage = FindName("BackgroundImage") as Image;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += OnHardwareBackPressed;
            _subsonicService = await PhoneSubsonicServiceFactory.CreatePlaybackServiceAsync();
            await LoadNowPlaying();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= OnHardwareBackPressed;
        }

        private void OnHardwareBackPressed(object sender, BackPressedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        private async Task LoadNowPlaying()
        {
            if (_loadingIndicator != null) _loadingIndicator.IsActive = true;
            if (_emptyStateText != null) _emptyStateText.Visibility = Visibility.Collapsed;

            try
            {
                var nowPlaying = await _subsonicService.GetNowPlayingAsync();
                Entries.Clear();

                foreach (var entry in nowPlaying)
                {
                    var displayItem = ToDisplayItem(entry);
                    Entries.Add(displayItem);

                    if (Entries.Count == 1)
                    {
                        SetImageSource(_backgroundImage, displayItem.CoverArt);
                    }
                }

                if (_entryCountText != null)
                {
                    _entryCountText.Text = Entries.Count == 1
                                              ? "1 active listener"
                                              : string.Format("{0} active listeners", Entries.Count);
                }

                if (_emptyStateText != null)
                {
                    _emptyStateText.Visibility = Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                if (_emptyStateText != null)
                {
                    _emptyStateText.Text = "Failed to load now playing.";
                    _emptyStateText.Visibility = Visibility.Visible;
                }

                await new MessageDialog("Failed to load now playing: " + ex.Message).ShowAsync();
            }
            finally
            {
                if (_loadingIndicator != null) _loadingIndicator.IsActive = false;
            }
        }

        private NowPlayingDisplayItem ToDisplayItem(Services.NowPlayingEntry entry)
        {
            var song = entry.Song ?? new Song();
            var coverArt = !string.IsNullOrEmpty(song.CoverArt)
                               ? _subsonicService.GetCoverArtUrl(song.CoverArt)
                               : "/Assets/CoverArtPlaceholder.jpg";

            return new NowPlayingDisplayItem
            {
                Id = song.Id,
                Title = song.Title ?? song.Name ?? "Unknown",
                Artist = song.Artist ?? "Unknown artist",
                Album = song.Album,
                Duration = song.Duration,
                DurationString = FormatDuration(song.Duration),
                CoverArt = coverArt,
                Activity = FormatActivity(entry)
            };
        }

        private static string FormatActivity(Services.NowPlayingEntry entry)
        {
            var username = string.IsNullOrEmpty(entry.Username) ? "someone" : entry.Username;
            var player = string.IsNullOrEmpty(entry.PlayerName) ? "Navidrome" : entry.PlayerName;
            var time = entry.MinutesAgo <= 0 ? "just now" : string.Format("{0} min ago", entry.MinutesAgo);
            return string.Format("{0} on {1} - {2}", username, player, time);
        }

        private static string FormatDuration(int seconds)
        {
            if (seconds <= 0) return string.Empty;
            var minutes = seconds / 60;
            var secs = seconds % 60;
            return string.Format("{0}:{1:D2}", minutes, secs);
        }

        private static void SetImageSource(Image image, string source)
        {
            if (image == null || string.IsNullOrEmpty(source))
            {
                return;
            }

            var uriKind = source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                              ? UriKind.Absolute
                              : UriKind.Relative;
            image.Source = new BitmapImage(new Uri(source, uriKind));
        }

        private async void OnEntryClick(object sender, ItemClickEventArgs e)
        {
            var entry = e.ClickedItem as NowPlayingDisplayItem;
            if (entry == null || string.IsNullOrEmpty(entry.Id))
            {
                await new MessageDialog("This now playing item cannot be played locally.").ShowAsync();
                return;
            }

            Frame.Navigate(
                typeof(PlaybackPage),
                new PlaybackNavigationParameter
                {
                    Playlist = new List<SongDisplayItem>
                    {
                        new SongDisplayItem
                        {
                            Id = entry.Id,
                            Title = entry.Title,
                            Artist = entry.Artist,
                            Duration = entry.Duration,
                            DurationString = entry.DurationString,
                            CoverArt = entry.CoverArt
                        }
                    },
                    StartIndex = 0
                });
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadNowPlaying();
        }
    }

    public class NowPlayingDisplayItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public int Duration { get; set; }
        public string DurationString { get; set; }
        public string CoverArt { get; set; }
        public string Activity { get; set; }
    }
}
