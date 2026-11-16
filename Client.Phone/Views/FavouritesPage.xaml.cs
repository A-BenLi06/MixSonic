namespace Mixdrome.Phone.Views
{
    using System;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading.Tasks;
    using Client.Common.Models.Subsonic;
    using Mixdrome.Phone.Services;
    using Windows.Phone.UI.Input;
    using Windows.UI.Popups;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Navigation;

    public sealed partial class FavouritesPage : Page
    {
        private WP8SubsonicService _subsonicService;
        private ProgressRing _loadingIndicator;
        private TextBlock _emptyStateText;
        private TextBlock _songCountText;

        public ObservableCollection<SongDisplayItem> Songs { get; private set; }

        public FavouritesPage()
        {
            Songs = new ObservableCollection<SongDisplayItem>();
            InitializeComponent();
            DataContext = this;
            _loadingIndicator = FindName("LoadingIndicator") as ProgressRing;
            _emptyStateText = FindName("EmptyStateText") as TextBlock;
            _songCountText = FindName("SongCountText") as TextBlock;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += OnHardwareBackPressed;
            _subsonicService = await PhoneSubsonicServiceFactory.CreatePlaybackServiceAsync();
            await LoadSongs();
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

        private async Task LoadSongs()
        {
            if (_loadingIndicator != null) _loadingIndicator.IsActive = true;
            if (_emptyStateText != null) _emptyStateText.Visibility = Visibility.Collapsed;

            try
            {
                var songs = await _subsonicService.GetStarredSongsAsync();
                Songs.Clear();

                var track = 1;
                foreach (var song in songs)
                {
                    Songs.Add(ToSongDisplayItem(song, track++));
                }

                if (_songCountText != null) _songCountText.Text = string.Format("{0} songs", Songs.Count);
                if (_emptyStateText != null) _emptyStateText.Visibility = Songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (_emptyStateText != null)
                {
                    _emptyStateText.Text = "Failed to load favourites.";
                    _emptyStateText.Visibility = Visibility.Visible;
                }

                await new MessageDialog("Failed to load favourites: " + ex.Message).ShowAsync();
            }
            finally
            {
                if (_loadingIndicator != null) _loadingIndicator.IsActive = false;
            }
        }

        private SongDisplayItem ToSongDisplayItem(Song song, int track)
        {
            return new SongDisplayItem
            {
                Id = song.Id,
                Track = track,
                Title = song.Title ?? song.Name ?? "Unknown",
                Artist = song.Artist ?? "Unknown artist",
                Duration = song.Duration,
                DurationString = FormatDuration(song.Duration),
                CoverArt = !string.IsNullOrEmpty(song.CoverArt)
                               ? _subsonicService.GetCoverArtUrl(song.CoverArt)
                               : "/Assets/CoverArtPlaceholder.jpg"
            };
        }

        private static string FormatDuration(int seconds)
        {
            var minutes = seconds / 60;
            var secs = seconds % 60;
            return string.Format("{0}:{1:D2}", minutes, secs);
        }

        private void NavigateToPlayback(int startIndex)
        {
            if (Songs.Count == 0) return;
            Frame.Navigate(
                typeof(PlaybackPage),
                new PlaybackNavigationParameter
                {
                    Playlist = Songs.ToList(),
                    StartIndex = startIndex
                });
        }

        private void OnSongClick(object sender, ItemClickEventArgs e)
        {
            var song = e.ClickedItem as SongDisplayItem;
            if (song == null) return;
            var index = Songs.IndexOf(song);
            NavigateToPlayback(index >= 0 ? index : 0);
        }

        private void OnPlayAllClick(object sender, RoutedEventArgs e)
        {
            NavigateToPlayback(0);
        }

        private void OnShuffleClick(object sender, RoutedEventArgs e)
        {
            if (Songs.Count == 0) return;
            var random = new Random();
            Frame.Navigate(
                typeof(PlaybackPage),
                new PlaybackNavigationParameter
                {
                    Playlist = Songs.OrderBy(x => random.Next()).ToList(),
                    StartIndex = 0
                });
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadSongs();
        }
    }
}
