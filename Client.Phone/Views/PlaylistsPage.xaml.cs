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

    public sealed partial class PlaylistsPage : Page
    {
        private ISubsonicService _subsonicService;
        private ProgressRing _loadingIndicator;
        private TextBlock _emptyStateText;

        public ObservableCollection<PlaylistDisplayItem> Playlists { get; private set; }

        public PlaylistsPage()
        {
            Playlists = new ObservableCollection<PlaylistDisplayItem>();
            InitializeComponent();
            DataContext = this;
            _loadingIndicator = FindName("LoadingIndicator") as ProgressRing;
            _emptyStateText = FindName("EmptyStateText") as TextBlock;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += OnHardwareBackPressed;
            _subsonicService = await PhoneSubsonicServiceFactory.CreateAsync();
            await LoadPlaylists();
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

        private async Task LoadPlaylists()
        {
            if (_loadingIndicator != null) _loadingIndicator.IsActive = true;
            if (_emptyStateText != null) _emptyStateText.Visibility = Visibility.Collapsed;

            try
            {
                var result = _subsonicService.GetAllPlaylists();
                await result.Execute();

                Playlists.Clear();
                if (result.Error == null && result.Result != null && result.Result.Playlists != null)
                {
                    foreach (var playlist in result.Result.Playlists)
                    {
                        Playlists.Add(new PlaylistDisplayItem
                        {
                            Id = playlist.Id,
                            Name = playlist.Name ?? "Untitled playlist",
                            Subtitle = string.Format("{0} songs", playlist.SongCount)
                        });
                    }
                }

                if (_emptyStateText != null) _emptyStateText.Visibility = Playlists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (_emptyStateText != null)
                {
                    _emptyStateText.Text = "Failed to load playlists.";
                    _emptyStateText.Visibility = Visibility.Visible;
                }

                await new MessageDialog("Failed to load playlists: " + ex.Message).ShowAsync();
            }
            finally
            {
                if (_loadingIndicator != null) _loadingIndicator.IsActive = false;
            }
        }

        private async void OnPlaylistClick(object sender, ItemClickEventArgs e)
        {
            var playlist = e.ClickedItem as PlaylistDisplayItem;
            if (playlist == null)
            {
                return;
            }

            if (_loadingIndicator != null) _loadingIndicator.IsActive = true;

            try
            {
                var result = _subsonicService.GetPlaylist(playlist.Id);
                await result.Execute();

                if (result.Error != null || result.Result == null || result.Result.Entries == null || result.Result.Entries.Count == 0)
                {
                    await new MessageDialog("This playlist is empty.").ShowAsync();
                    return;
                }

                var track = 1;
                var songs = result.Result.Entries.Select(entry => new SongDisplayItem
                {
                    Id = entry.Id,
                    Track = track++,
                    Title = entry.Title ?? entry.Name ?? "Unknown",
                    Artist = entry.Artist ?? "Unknown artist",
                    Duration = entry.Duration,
                    DurationString = FormatDuration(entry.Duration),
                    CoverArt = entry.CoverArt
                }).ToList();

                Frame.Navigate(
                    typeof(PlaybackPage),
                    new PlaybackNavigationParameter
                    {
                        Playlist = songs,
                        StartIndex = 0
                    });
            }
            catch (Exception ex)
            {
                await new MessageDialog("Failed to load playlist: " + ex.Message).ShowAsync();
            }
            finally
            {
                if (_loadingIndicator != null) _loadingIndicator.IsActive = false;
            }
        }

        private static string FormatDuration(int seconds)
        {
            var minutes = seconds / 60;
            var secs = seconds % 60;
            return string.Format("{0}:{1:D2}", minutes, secs);
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadPlaylists();
        }
    }

    public class PlaylistDisplayItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Subtitle { get; set; }
    }
}
