namespace Mixdrome.Phone.Views
{
    using System;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading.Tasks;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services;
    using Mixdrome.Phone.Services;
    using Windows.Phone.UI.Input;
    using Windows.UI.Popups;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Navigation;

    public sealed partial class GenreSongsPage : Page
    {
        private ISubsonicService _subsonicService;
        private string _genre;
        private ProgressRing _loadingIndicator;
        private TextBlock _emptyStateText;
        private TextBlock _genreNameText;
        private TextBlock _songCountText;

        public ObservableCollection<SongDisplayItem> Songs { get; private set; }

        public GenreSongsPage()
        {
            Songs = new ObservableCollection<SongDisplayItem>();
            InitializeComponent();
            DataContext = this;
            _loadingIndicator = FindName("LoadingIndicator") as ProgressRing;
            _emptyStateText = FindName("EmptyStateText") as TextBlock;
            _genreNameText = FindName("GenreNameText") as TextBlock;
            _songCountText = FindName("SongCountText") as TextBlock;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += OnHardwareBackPressed;
            _genre = e.Parameter as string;
            if (_genreNameText != null) _genreNameText.Text = string.IsNullOrEmpty(_genre) ? "genre" : _genre;
            _subsonicService = await PhoneSubsonicServiceFactory.CreateAsync();
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
            if (string.IsNullOrEmpty(_genre))
            {
                if (_emptyStateText != null) _emptyStateText.Visibility = Visibility.Visible;
                return;
            }

            if (_loadingIndicator != null) _loadingIndicator.IsActive = true;

            try
            {
                var result = _subsonicService.GetSongsByGenre(_genre);
                await result.Execute();
                Songs.Clear();

                if (result.Error == null && result.Result != null)
                {
                    var track = 1;
                    foreach (var song in result.Result)
                    {
                        Songs.Add(ToSongDisplayItem(song, track++));
                    }
                }

                if (_songCountText != null) _songCountText.Text = string.Format("{0} songs", Songs.Count);
                if (_emptyStateText != null) _emptyStateText.Visibility = Songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (_emptyStateText != null)
                {
                    _emptyStateText.Text = "Failed to load songs.";
                    _emptyStateText.Visibility = Visibility.Visible;
                }

                await new MessageDialog("Failed to load genre: " + ex.Message).ShowAsync();
            }
            finally
            {
                if (_loadingIndicator != null) _loadingIndicator.IsActive = false;
            }
        }

        private static SongDisplayItem ToSongDisplayItem(Song song, int track)
        {
            return new SongDisplayItem
            {
                Id = song.Id,
                Track = track,
                Title = song.Title ?? song.Name ?? "Unknown",
                Artist = song.Artist ?? "Unknown artist",
                Duration = song.Duration,
                DurationString = FormatDuration(song.Duration),
                CoverArt = song.CoverArt
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
            if (song != null)
            {
                var index = Songs.IndexOf(song);
                NavigateToPlayback(index >= 0 ? index : 0);
            }
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
    }
}
