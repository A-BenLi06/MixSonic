namespace Mixdrome.Phone.Views
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading.Tasks;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Windows.Phone.UI.Input;
    using Windows.Storage;
    using Windows.System;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;
    using Windows.UI.Xaml.Media;
    using Windows.UI.Xaml.Navigation;

    public sealed partial class SearchPage : Page
    {
        private ISubsonicService _subsonicService;
        private TextBox _searchTextBox;
        private ProgressRing _loadingIndicator;
        private TextBlock _emptyStateText;
        private ListView _resultsListView;

        public ObservableCollection<SearchResultDisplayItem> Results { get; private set; }
        public List<SongDisplayItem> SongResults { get; private set; }

        public SearchPage()
        {
            Results = new ObservableCollection<SearchResultDisplayItem>();
            SongResults = new List<SongDisplayItem>();

            InitializeComponent();
            DataContext = this;

            _searchTextBox = FindName("SearchTextBox") as TextBox;
            _loadingIndicator = FindName("LoadingIndicator") as ProgressRing;
            _emptyStateText = FindName("EmptyStateText") as TextBlock;
            _resultsListView = FindName("ResultsListView") as ListView;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += OnHardwareBackPressed;

            await InitializeService();

            var query = e.Parameter as string;
            if (!string.IsNullOrWhiteSpace(query) && _searchTextBox != null)
            {
                _searchTextBox.Text = query;
                await Search(query);
            }
            else if (_searchTextBox != null)
            {
                _searchTextBox.Focus(FocusState.Programmatic);
            }
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
                config.Username = settings.Values.ContainsKey("Username")
                    ? settings.Values["Username"] as string
                    : string.Empty;
                config.Password = settings.Values.ContainsKey("Password")
                    ? settings.Values["Password"] as string
                    : string.Empty;
                config.CompatibleMode = settings.Values.ContainsKey("CompatibleMode")
                    && (bool)settings.Values["CompatibleMode"];

                var primaryUrl = settings.Values.ContainsKey("PrimaryUrl")
                    ? settings.Values["PrimaryUrl"] as string
                    : string.Empty;
                var secondaryUrl = settings.Values.ContainsKey("SecondaryUrl")
                    ? settings.Values["SecondaryUrl"] as string
                    : string.Empty;

                if (string.IsNullOrEmpty(primaryUrl) && settings.Values.ContainsKey("BaseUrl"))
                {
                    primaryUrl = settings.Values["BaseUrl"] as string;
                }

                config.PrimaryUrl = primaryUrl;
                config.SecondaryUrl = secondaryUrl;

                if (!string.IsNullOrEmpty(primaryUrl) || !string.IsNullOrEmpty(secondaryUrl))
                {
                    var networkService = new NetworkDetectionService();
                    var routeService = new RouteSelectionService(networkService);
                    var result = await routeService.SelectBestRouteAsync(
                        primaryUrl,
                        secondaryUrl,
                        config.Username,
                        config.Password);

                    config.BaseUrl = result.Success
                        ? result.SelectedUrl
                        : (!string.IsNullOrEmpty(secondaryUrl) ? secondaryUrl : primaryUrl);
                }
            }
            catch
            {
                var settings = ApplicationData.Current.LocalSettings;
                config.BaseUrl = settings.Values.ContainsKey("PrimaryUrl")
                    ? settings.Values["PrimaryUrl"] as string
                    : string.Empty;
                config.PrimaryUrl = config.BaseUrl;
            }

            return config;
        }

        private bool HasConfiguredServer()
        {
            return _subsonicService != null
                   && _subsonicService.Configuration != null
                   && !string.IsNullOrEmpty(_subsonicService.Configuration.BaseUrl);
        }

        private async Task Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                ShowEmpty("Type something to search your music.");
                return;
            }

            if (!HasConfiguredServer())
            {
                ShowEmpty("Set up your Navidrome server in settings first.");
                return;
            }

            if (_loadingIndicator != null) _loadingIndicator.IsActive = true;
            if (_emptyStateText != null) _emptyStateText.Visibility = Visibility.Collapsed;

            try
            {
                var result = _subsonicService.Search(query.Trim());
                await result.Execute();

                Results.Clear();
                SongResults.Clear();

                if (result.Error != null)
                {
                    ShowEmpty("Search failed: " + result.Error.Message);
                    return;
                }

                var collection = result.Result;
                if (collection != null)
                {
                    AddArtists(collection.Artists);
                    AddAlbums(collection.Albums);
                    AddSongs(collection.Songs);
                }

                if (_resultsListView != null)
                {
                    _resultsListView.Visibility = Results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                }

                if (Results.Count == 0)
                {
                    ShowEmpty("No results found.");
                }
            }
            catch (Exception ex)
            {
                ShowEmpty("Search failed: " + ex.Message);
            }
            finally
            {
                if (_loadingIndicator != null) _loadingIndicator.IsActive = false;
            }
        }

        private void AddArtists(IEnumerable<ExpandedArtist> artists)
        {
            if (artists == null)
            {
                return;
            }

            foreach (var artist in artists)
            {
                Results.Add(new SearchResultDisplayItem
                {
                    Kind = SearchResultKind.Artist,
                    KindLabel = "artist",
                    Id = artist.Id,
                    Title = artist.Name ?? "Unknown artist",
                    Subtitle = string.Format("{0} albums", artist.AlbumCount),
                    Glyph = "\uE13D",
                    AccentBrush = App.Current.Resources["MixRadioYellowBrush"] as Brush,
                    ImageOpacity = 0
                });
            }
        }

        private void AddAlbums(IEnumerable<Album> albums)
        {
            if (albums == null)
            {
                return;
            }

            foreach (var album in albums)
            {
                Results.Add(new SearchResultDisplayItem
                {
                    Kind = SearchResultKind.Album,
                    KindLabel = "album",
                    Id = album.Id,
                    Title = album.Name ?? "Unknown album",
                    Subtitle = album.Artist ?? "Unknown artist",
                    CoverArt = GetCoverArtUrl(album.CoverArt),
                    Glyph = string.Empty,
                    AccentBrush = App.Current.Resources["MixRadioTealBrush"] as Brush,
                    ImageOpacity = 1
                });
            }
        }

        private void AddSongs(IEnumerable<Song> songs)
        {
            if (songs == null)
            {
                return;
            }

            var track = 1;
            foreach (var song in songs)
            {
                SongResults.Add(new SongDisplayItem
                {
                    Id = song.Id,
                    Track = track,
                    Title = song.Title ?? song.Name ?? "Unknown",
                    Artist = song.Artist ?? "Unknown artist",
                    Duration = song.Duration,
                    DurationString = FormatDuration(song.Duration),
                    CoverArt = song.CoverArt
                });

                Results.Add(new SearchResultDisplayItem
                {
                    Kind = SearchResultKind.Song,
                    KindLabel = "song",
                    Id = song.Id,
                    Title = song.Title ?? song.Name ?? "Unknown",
                    Subtitle = song.Artist ?? "Unknown artist",
                    CoverArt = GetCoverArtUrl(song.CoverArt),
                    Glyph = string.IsNullOrEmpty(song.CoverArt) ? "\uE102" : string.Empty,
                    AccentBrush = App.Current.Resources["MixRadioAccentBrush"] as Brush,
                    ImageOpacity = string.IsNullOrEmpty(song.CoverArt) ? 0 : 1
                });
                track++;
            }
        }

        private string GetCoverArtUrl(string coverArt)
        {
            return !string.IsNullOrEmpty(coverArt)
                ? _subsonicService.GetCoverArtForId(coverArt)
                : "/Assets/CoverArtPlaceholder.jpg";
        }

        private static string FormatDuration(int seconds)
        {
            var minutes = seconds / 60;
            var secs = seconds % 60;
            return string.Format("{0}:{1:D2}", minutes, secs);
        }

        private void ShowEmpty(string text)
        {
            if (_emptyStateText != null)
            {
                _emptyStateText.Text = text;
                _emptyStateText.Visibility = Visibility.Visible;
            }

            if (_resultsListView != null)
            {
                _resultsListView.Visibility = Visibility.Collapsed;
            }
        }

        private async void OnSearchButtonClick(object sender, RoutedEventArgs e)
        {
            await Search(_searchTextBox != null ? _searchTextBox.Text : string.Empty);
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            if (_searchTextBox != null)
            {
                _searchTextBox.Text = string.Empty;
                _searchTextBox.Focus(FocusState.Programmatic);
            }

            Results.Clear();
            SongResults.Clear();
            ShowEmpty("Type something to search your music.");
        }

        private async void OnSearchTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await Search(_searchTextBox != null ? _searchTextBox.Text : string.Empty);
            }
        }

        private void OnResultClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as SearchResultDisplayItem;
            if (item == null)
            {
                return;
            }

            if (item.Kind == SearchResultKind.Album)
            {
                Frame.Navigate(typeof(AlbumPage), item.Id);
                return;
            }

            if (item.Kind == SearchResultKind.Song)
            {
                var startIndex = Math.Max(0, SongResults.FindIndex(song => song.Id == item.Id));
                Frame.Navigate(
                    typeof(PlaybackPage),
                    new PlaybackNavigationParameter
                    {
                        Playlist = SongResults.ToList(),
                        StartIndex = startIndex
                    });
                return;
            }

            Frame.Navigate(
                typeof(ArtistAlbumsPage),
                new ArtistDisplayItem
                {
                    Id = item.Id,
                    Name = item.Title,
                    Subtitle = item.Subtitle,
                    Initial = GetInitial(item.Title)
                });
        }

        private static string GetInitial(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? "?" : name.Trim().Substring(0, 1).ToUpperInvariant();
        }
    }

    public enum SearchResultKind
    {
        Artist,
        Album,
        Song
    }

    public class SearchResultDisplayItem
    {
        public SearchResultKind Kind { get; set; }
        public string KindLabel { get; set; }
        public string Id { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string CoverArt { get; set; }
        public string Glyph { get; set; }
        public Brush AccentBrush { get; set; }
        public double ImageOpacity { get; set; }
    }
}
