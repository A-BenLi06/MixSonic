namespace Subsonic8.Phone.Views
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Threading.Tasks;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Subsonic8.Phone.Services;
    using Windows.Storage;
    using Windows.UI.Popups;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media.Imaging;
    using Windows.UI.Xaml.Navigation;

    public sealed partial class MainPage : Page
    {
        private ISubsonicService _subsonicService;

        // Control references
        private ProgressRing _loadingIndicator;
        private ScrollViewer _homePanoramaScrollViewer;
        private ListView _albumListView;
        private TextBlock _emptyStateText;
        private TextBlock _albumCountText;
        private TextBlock _artistCountText;
        private TextBlock _genreCountText;
        private TextBlock _playlistCountText;
        private TextBlock _favouriteCountText;
        private TextBlock _radioCountText;
        private TextBlock _nowPlayingCountText;
        private Image _heroBackgroundImage;
        private Image _albumTileImage;
        private Image _artistTileImage;
        private Image _favouriteTileImage;
        private Image _genreTileImage;

        public ObservableCollection<Album> Albums { get; } = new ObservableCollection<Album>();

        public MainPage()
        {
            InitializeComponent();
            DataContext = this;

            // Get control references
            _loadingIndicator = FindName("LoadingIndicator") as ProgressRing;
            _homePanoramaScrollViewer = FindName("HomePanoramaScrollViewer") as ScrollViewer;
            _albumListView = FindName("AlbumListView") as ListView;
            _emptyStateText = FindName("EmptyStateText") as TextBlock;
            _albumCountText = FindName("AlbumCountText") as TextBlock;
            _artistCountText = FindName("ArtistCountText") as TextBlock;
            _genreCountText = FindName("GenreCountText") as TextBlock;
            _playlistCountText = FindName("PlaylistCountText") as TextBlock;
            _favouriteCountText = FindName("FavouriteCountText") as TextBlock;
            _radioCountText = FindName("RadioCountText") as TextBlock;
            _nowPlayingCountText = FindName("NowPlayingCountText") as TextBlock;
            _heroBackgroundImage = FindName("HeroBackgroundImage") as Image;
            _albumTileImage = FindName("AlbumTileImage") as Image;
            _artistTileImage = FindName("ArtistTileImage") as Image;
            _favouriteTileImage = FindName("FavouriteTileImage") as Image;
            _genreTileImage = FindName("GenreTileImage") as Image;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            await InitializeService();
            await LoadAlbums();
            await LoadLibraryCounts();
        }

        private async Task InitializeService()
        {
            var configuration = await LoadConfiguration();
            _subsonicService = new SubsonicService();
            _subsonicService.Configuration = configuration;
        }

        private async Task<SubsonicServiceConfiguration> LoadConfiguration()
        {
            var config = new SubsonicServiceConfiguration();

            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                
                // Load credentials
                config.Username = settings.Values.ContainsKey("Username") 
                    ? settings.Values["Username"] as string : string.Empty;
                config.Password = settings.Values.ContainsKey("Password") 
                    ? settings.Values["Password"] as string : string.Empty;
                config.CompatibleMode = settings.Values.ContainsKey("CompatibleMode") 
                    && (bool)settings.Values["CompatibleMode"];

                // Load dual URLs
                var primaryUrl = settings.Values.ContainsKey("PrimaryUrl") 
                    ? settings.Values["PrimaryUrl"] as string : string.Empty;
                var secondaryUrl = settings.Values.ContainsKey("SecondaryUrl") 
                    ? settings.Values["SecondaryUrl"] as string : string.Empty;
                
                // Backwards compatibility: migrate old BaseUrl
                if (string.IsNullOrEmpty(primaryUrl) && settings.Values.ContainsKey("BaseUrl"))
                {
                    primaryUrl = settings.Values["BaseUrl"] as string;
                }

                config.PrimaryUrl = primaryUrl;
                config.SecondaryUrl = secondaryUrl;

                // Perform route selection to find working URL
                if (!string.IsNullOrEmpty(primaryUrl) || !string.IsNullOrEmpty(secondaryUrl))
                {
                    var networkService = new NetworkDetectionService();
                    var routeService = new RouteSelectionService(networkService);

                    var result = await routeService.SelectBestRouteAsync(
                        primaryUrl,
                        secondaryUrl,
                        config.Username,
                        config.Password);

                    if (result.Success)
                    {
                        config.BaseUrl = result.SelectedUrl;
                    }
                    else
                    {
                        // Fallback: prefer SecondaryUrl (external/DDNS) over PrimaryUrl (internal)
                        config.BaseUrl = !string.IsNullOrEmpty(secondaryUrl) ? secondaryUrl : primaryUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error for debugging
                System.Diagnostics.Debug.WriteLine("LoadConfiguration error: " + ex.Message);
                
                // Ensure BaseUrl is set even if route selection fails
                try
                {
                    var settings = ApplicationData.Current.LocalSettings;
                    var primaryUrl = settings.Values.ContainsKey("PrimaryUrl") 
                        ? settings.Values["PrimaryUrl"] as string : string.Empty;
                    var secondaryUrl = settings.Values.ContainsKey("SecondaryUrl") 
                        ? settings.Values["SecondaryUrl"] as string : string.Empty;
                    
                    // Backwards compatibility
                    if (string.IsNullOrEmpty(primaryUrl) && settings.Values.ContainsKey("BaseUrl"))
                    {
                        primaryUrl = settings.Values["BaseUrl"] as string;
                    }
                    
                    // Prefer SecondaryUrl (DDNS) for external network fallback
                    config.BaseUrl = !string.IsNullOrEmpty(secondaryUrl) ? secondaryUrl : primaryUrl;
                    config.PrimaryUrl = primaryUrl;
                    config.SecondaryUrl = secondaryUrl;
                    
                    // Load credentials if not already loaded
                    if (string.IsNullOrEmpty(config.Username))
                    {
                        config.Username = settings.Values.ContainsKey("Username") 
                            ? settings.Values["Username"] as string : string.Empty;
                        config.Password = settings.Values.ContainsKey("Password") 
                            ? settings.Values["Password"] as string : string.Empty;
                    }
                }
                catch
                {
                    // Last resort fallback - use defaults
                }
            }

            return config;
        }

        private async Task LoadAlbums()
        {
            if (string.IsNullOrEmpty(_subsonicService.Configuration?.BaseUrl))
            {
                if (_emptyStateText != null) _emptyStateText.Visibility = Visibility.Visible;
                if (_albumListView != null) _albumListView.Visibility = Visibility.Collapsed;
                if (_albumCountText != null) _albumCountText.Text = "set up";
                if (_artistCountText != null) _artistCountText.Text = "set up";
                if (_genreCountText != null) _genreCountText.Text = "set up";
                if (_playlistCountText != null) _playlistCountText.Text = "set up";
                if (_favouriteCountText != null) _favouriteCountText.Text = "set up";
                if (_radioCountText != null) _radioCountText.Text = "set up";
                if (_nowPlayingCountText != null) _nowPlayingCountText.Text = "set up";
                return;
            }

            if (_loadingIndicator != null) _loadingIndicator.IsActive = true;
            if (_emptyStateText != null) _emptyStateText.Visibility = Visibility.Collapsed;

            try
            {
                var result = _subsonicService.GetAlbumList(string.Empty);
                await result.Execute();

                if (result.Error == null && result.Result != null)
                {
                    Albums.Clear();
                    var coverArts = new List<string>();
                    foreach (var album in result.Result)
                    {
                        album.CoverArt = _subsonicService.GetCoverArtForId(album.CoverArt);
                        if (!string.IsNullOrEmpty(album.CoverArt))
                        {
                            coverArts.Add(album.CoverArt);
                        }

                        Albums.Add(album);
                    }

                    ApplyFeaturedCoverArt(coverArts);
                }

                if (_emptyStateText != null) _emptyStateText.Visibility = Albums.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (_albumListView != null) _albumListView.Visibility = Albums.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (_albumCountText != null) _albumCountText.Text = string.Format("{0} albums", Albums.Count);
            }
            catch (Exception ex)
            {
                await new MessageDialog("Failed to load albums: " + ex.Message).ShowAsync();
                if (_albumCountText != null) _albumCountText.Text = "offline";
            }
            finally
            {
                if (_loadingIndicator != null) _loadingIndicator.IsActive = false;
            }
        }

        private async Task LoadLibraryCounts()
        {
            if (!HasConfiguredServer())
            {
                return;
            }

            try
            {
                var artistsResult = _subsonicService.GetArtists();
                await artistsResult.Execute();
                if (_artistCountText != null)
                {
                    _artistCountText.Text = artistsResult.Error == null && artistsResult.Result != null
                        ? string.Format("{0} artists", artistsResult.Result.Count)
                        : "offline";
                }
            }
            catch
            {
                if (_artistCountText != null) _artistCountText.Text = "offline";
            }

            try
            {
                var genresResult = _subsonicService.GetGenres();
                await genresResult.Execute();
                if (_genreCountText != null)
                {
                    _genreCountText.Text = genresResult.Error == null && genresResult.Result != null
                        ? string.Format("{0} genres", genresResult.Result.Count)
                        : "offline";
                }
            }
            catch
            {
                if (_genreCountText != null) _genreCountText.Text = "offline";
            }

            try
            {
                var playlistsResult = _subsonicService.GetAllPlaylists();
                await playlistsResult.Execute();
                if (_playlistCountText != null)
                {
                    var count = playlistsResult.Error == null
                                && playlistsResult.Result != null
                                && playlistsResult.Result.Playlists != null
                                    ? playlistsResult.Result.Playlists.Count
                                    : -1;
                    _playlistCountText.Text = count >= 0 ? string.Format("{0} playlists", count) : "offline";
                }
            }
            catch
            {
                if (_playlistCountText != null) _playlistCountText.Text = "offline";
            }

            try
            {
                var playbackService = await PhoneSubsonicServiceFactory.CreatePlaybackServiceAsync();
                var favouriteSongs = await playbackService.GetStarredSongsAsync();
                if (_favouriteCountText != null)
                {
                    _favouriteCountText.Text = string.Format("{0} songs", favouriteSongs.Count);
                }
            }
            catch
            {
                if (_favouriteCountText != null) _favouriteCountText.Text = "offline";
            }

            try
            {
                var playbackService = await PhoneSubsonicServiceFactory.CreatePlaybackServiceAsync();
                var radioStations = await playbackService.GetInternetRadioStationsAsync();
                if (_radioCountText != null)
                {
                    _radioCountText.Text = string.Format("{0} stations", radioStations.Count);
                }
            }
            catch
            {
                if (_radioCountText != null) _radioCountText.Text = "offline";
            }

            try
            {
                var playbackService = await PhoneSubsonicServiceFactory.CreatePlaybackServiceAsync();
                var nowPlaying = await playbackService.GetNowPlayingAsync();
                if (_nowPlayingCountText != null)
                {
                    _nowPlayingCountText.Text = nowPlaying.Count == 1
                                                    ? "1 listener"
                                                    : string.Format("{0} listeners", nowPlaying.Count);
                }
            }
            catch
            {
                if (_nowPlayingCountText != null) _nowPlayingCountText.Text = "offline";
            }
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

        private void ApplyFeaturedCoverArt(IList<string> coverArts)
        {
            if (coverArts == null || coverArts.Count == 0)
            {
                return;
            }

            SetImageSource(_heroBackgroundImage, coverArts[0]);
            SetImageSource(_albumTileImage, coverArts[0]);
            SetImageSource(_artistTileImage, GetCoverArtAt(coverArts, 1));
            SetImageSource(_favouriteTileImage, GetCoverArtAt(coverArts, 2));
            SetImageSource(_genreTileImage, GetCoverArtAt(coverArts, 3));
        }

        private static string GetCoverArtAt(IList<string> coverArts, int index)
        {
            return coverArts[index % coverArts.Count];
        }

        private bool HasConfiguredServer()
        {
            return _subsonicService != null
                   && _subsonicService.Configuration != null
                   && !string.IsNullOrEmpty(_subsonicService.Configuration.BaseUrl);
        }

        private static SongDisplayItem ToSongDisplayItem(Song song, int track)
        {
            return new SongDisplayItem
            {
                Id = song.Id,
                Track = track,
                Title = song.Title ?? song.Name ?? "Unknown",
                Artist = song.Artist ?? "Unknown Artist",
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

        private void OnAlbumClick(object sender, ItemClickEventArgs e)
        {
            var album = e.ClickedItem as Album;
            if (album != null)
            {
                Frame.Navigate(typeof(AlbumPage), album.Id);
            }
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SearchPage));
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadAlbums();
            await LoadLibraryCounts();
        }

        private async void OnPlayMeClick(object sender, RoutedEventArgs e)
        {
            if (!HasConfiguredServer())
            {
                await new MessageDialog("Set up your Navidrome server in settings first.").ShowAsync();
                return;
            }

            if (_loadingIndicator != null) _loadingIndicator.IsActive = true;

            try
            {
                var result = _subsonicService.GetRandomSongs(30);
                await result.Execute();

                if (result.Error != null || result.Result == null || result.Result.Count == 0)
                {
                    await new MessageDialog("No songs were returned for play me.").ShowAsync();
                    return;
                }

                var playlist = new List<SongDisplayItem>();
                var track = 1;
                foreach (var song in result.Result)
                {
                    playlist.Add(ToSongDisplayItem(song, track++));
                }

                Frame.Navigate(
                    typeof(PlaybackPage),
                    new PlaybackNavigationParameter
                    {
                        Playlist = playlist,
                        StartIndex = 0
                    });
            }
            catch (Exception ex)
            {
                await new MessageDialog("Failed to start play me: " + ex.Message).ShowAsync();
            }
            finally
            {
                if (_loadingIndicator != null) _loadingIndicator.IsActive = false;
            }
        }

        private void OnAlbumsHubClick(object sender, RoutedEventArgs e)
        {
            if (_homePanoramaScrollViewer != null)
            {
                _homePanoramaScrollViewer.ChangeView(0, null, null);
            }

            if (_albumListView != null && Albums.Count > 0)
            {
                _albumListView.ScrollIntoView(Albums[0]);
            }
        }

        private void OnArtistsClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ArtistsPage));
        }

        private void OnGenresClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(GenresPage));
        }

        private void OnPlaylistsClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PlaylistsPage));
        }

        private void OnFavouritesClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(FavouritesPage));
        }

        private void OnRadioClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(RadioStationsPage));
        }

        private void OnNowPlayingClick(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(NowPlayingPage));
        }
    }
}
