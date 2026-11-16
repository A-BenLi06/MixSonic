namespace Mixdrome.Phone.Views
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Client.Common.Models.Navidrome;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.Navidrome;
    using Mixdrome.Phone.Common;
    using Mixdrome.Phone.Services;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Navigation;
    public sealed partial class MainPage : Page
    {
        private PhoneHomePanel home;
        private IList<Album> history = new List<Album>();
        private int generation;
        private bool active;
        private bool startingPlayback;
        public MainPage() { InitializeComponent(); SizeChanged += OnSizeChanged; }
        protected override async void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); active = true; ResizeSections(); await LoadHome(); }
        protected override void OnNavigatedFrom(NavigationEventArgs e) { active = false; generation++; base.OnNavigatedFrom(e); }
        private bool IsCurrent(int version) { return active && generation == version; }
        private async Task LoadHome()
        {
            var version = ++generation;
            LoadingIndicator.IsActive = true; LoadingIndicator.Visibility = Visibility.Visible;
            StatusText.Text = string.Empty;
            history = new List<Album>(); if (home != null) home.SetHistory(history);
            try
            {
                var service = await PhoneSubsonicServiceFactory.CreateAsync();
                if (!IsCurrent(version)) return;
                if (string.IsNullOrEmpty(service.Configuration.BaseUrl)) { StatusText.Text = PhoneText.Get("ConfigureServer"); return; }
                var albums = await new NavidromeApi(service.Configuration).GetAlbums(new AlbumQuery { Type = AlbumListType.Recent, Size = 3 }).ReadAsync();
                if (!IsCurrent(version)) return;
                foreach (var album in albums) album.CoverArt = service.GetCoverArtForId(album.CoverArt);
                history = albums; if (home != null) home.SetHistory(history);
                StatusText.Text = albums.Count == 0 ? PhoneText.Get("NoRecentAlbums") : string.Empty;
            }
            catch (Exception ex) { if (IsCurrent(version)) StatusText.Text = PhoneText.Format("LoadFailed", ex.Message); }
            finally { if (IsCurrent(version)) { LoadingIndicator.IsActive = false; LoadingIndicator.Visibility = Visibility.Collapsed; } }
        }
        private void OnHomePanelLoaded(object sender, RoutedEventArgs e)
        {
            var panel = (PhoneHomePanel)sender;
            panel.TileClicked -= OnTileClicked; panel.TileClicked += OnTileClicked;
            home = panel; panel.SetHistory(history); panel.IsBusy = startingPlayback;
        }
        private async void OnTileClicked(object sender, PhoneTileEventArgs e)
        {
            if (e.Route == "playme") { await PlayMe(); return; }
            if (e.Route == "album" && e.Album != null) { Frame.Navigate(typeof(AlbumPage), e.Album.Id); return; }
            Navigate(e.Route);
        }
        private async Task PlayMe()
        {
            if (startingPlayback) return;
            var version = generation; startingPlayback = true; if (home != null) home.IsBusy = true;
            try
            {
                var service = await PhoneSubsonicServiceFactory.CreateAsync();
                if (!IsCurrent(version)) return;
                if (string.IsNullOrEmpty(service.Configuration.BaseUrl)) { StatusText.Text = PhoneText.Get("ConfigureServer"); return; }
                var songs = await new NavidromeApi(service.Configuration).GetRandomSongs(new RandomSongQuery { Size = 30 }).ReadAsync();
                if (!IsCurrent(version)) return;
                if (songs.Count == 0) { StatusText.Text = PhoneText.Get("NoSongs"); return; }
                Frame.Navigate(typeof(PlaybackPage), new PlaybackNavigationParameter { Playlist = songs.Select((song, index) => new SongDisplayItem { Id = song.Id, Track = index + 1, Title = song.Title ?? song.Name, Artist = song.Artist, Album = song.Album, CoverArt = song.CoverArt, Duration = song.Duration }).ToList(), StartIndex = 0 });
            }
            catch (Exception ex) { if (IsCurrent(version)) StatusText.Text = PhoneText.Format("LoadFailed", ex.Message); }
            finally { startingPlayback = false; if (home != null) home.IsBusy = false; }
        }
        private void Navigate(string route)
        {
            switch (route)
            {
                case "library": case "artists": Frame.Navigate(typeof(LibraryPage), "artists"); break;
                case "albums": Frame.Navigate(typeof(LibraryPage), "albums"); break;
                case "recent": Frame.Navigate(typeof(LibraryPage), "recent"); break;
                case "genres": Frame.Navigate(typeof(LibraryPage), "genres"); break;
                case "playlists": Frame.Navigate(typeof(LibraryPage), "playlists"); break;
                case "favourites": Frame.Navigate(typeof(FavouritesPage)); break;
                case "radio": Frame.Navigate(typeof(RadioStationsPage)); break;
                case "activity": Frame.Navigate(typeof(NowPlayingPage)); break;
                case "player": Frame.Navigate(typeof(PlaybackPage)); break;
                case "search": Frame.Navigate(typeof(SearchPage)); break;
            }
        }
        private void OnMenuTileSizeChanged(object sender, SizeChangedEventArgs e) { var tile = (Button)sender; if (tile.ActualWidth > 0 && Math.Abs(tile.Height - tile.ActualWidth) > 0.5) tile.Height = tile.ActualWidth; }
        private void OnMenuClick(object sender, RoutedEventArgs e) { Navigate(((Button)sender).Tag as string); }
        private void OnSearchClick(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(SearchPage)); }
        private void OnSettingsClick(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(SettingsPage)); }
        private async void OnRefreshClick(object sender, RoutedEventArgs e) { await LoadHome(); }
        private void OnSizeChanged(object sender, SizeChangedEventArgs e) { ResizeSections(); }
        private void ResizeSections() { if (ActualWidth > 0) { PlaySection.Width = ActualWidth * 420 / 480; MusicSection.Width = ActualWidth * 360 / 480; } }
    }
}
