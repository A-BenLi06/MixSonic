namespace Mixdrome.Phone.Common
{
    using System;
    using System.Collections.Generic;
    using Client.Common.Models.Subsonic;
    using Mixdrome.Phone.Services;
    using Windows.Foundation;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Automation;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media;
    using Windows.UI.Xaml.Media.Imaging;
    public sealed class PhoneTileEventArgs : EventArgs
    {
        public string Route { get; set; }
        public Album Album { get; set; }
        public Playlist Playlist { get; set; }
    }
    public sealed partial class PhoneHomePanel : UserControl
    {
        private bool played;
        private bool busy;
        private string artwork;
        public event EventHandler<PhoneTileEventArgs> TileClicked;
        public PhoneHomePanel()
        {
            InitializeComponent(); SizeChanged += OnPanelSizeChanged;
            CreateMixLabel.Text = PhoneText.Get("UiCreateMix");
            MixOneLabel.Text = PhoneText.Get("UiMixes"); MixTwoLabel.Text = PhoneText.Get("UiOfflineMixes");
        }
        private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
        {
            var scale = e.NewSize.Width / 420;
            if (scale <= 0 || double.IsInfinity(scale) || double.IsNaN(scale)) return;
            var gap = 12 * scale;
            LargeTileColumn.Width = new GridLength(240 * scale); SmallTileColumn.Width = new GridLength(180 * scale);
            OffsetColumn.Margin = new Thickness(0, 24 * scale, 0, 0);
            SizeTile(PlayMeButton, 228 * scale, gap); SizeTile(CreateMixButton, 168 * scale, gap);
            SizeTile(MixOneButton, 168 * scale, gap); SizeTile(MixTwoButton, 168 * scale, gap);
            SizeTile(RecentAlbumOne, 96 * scale, gap); SizeTile(RecentAlbumTwo, 96 * scale, gap); SizeTile(RecentAlbumThree, 96 * scale, gap);
            foreach (var label in new[] { PlayMeLabel, CreateMixLabel, MixOneLabel, MixTwoLabel })
            { label.FontSize = 21 * scale; label.Margin = new Thickness(12 * scale, 0, 8 * scale, 10 * scale); }
            RecentLabel.FontSize = 24 * scale; RecentLink.Margin = new Thickness(0, 48 * scale, 0, 24 * scale);
            MixOneBand.Height = MixTwoBand.Height = 50 * scale;
            CurrentElapsed.FontSize = 124 * scale;
            CurrentElapsed.Margin = new Thickness(-100 * scale, 0, 0, -34 * scale);
            CurrentClip.Clip = new RectangleGeometry { Rect = new Rect(0, 0, 228 * scale, 228 * scale) };
        }
        private static void SizeTile(FrameworkElement tile, double size, double gap) { tile.Width = tile.Height = size; tile.Margin = new Thickness(0, 0, gap, gap); }
        private void OnMixLabelSizeChanged(object sender, SizeChangedEventArgs args)
        {
            var label = (TextBlock)sender;
            var band = ReferenceEquals(label, MixOneLabel) ? MixOneBand : MixTwoBand;
            var scale = ActualWidth / 420;
            if (scale > 0) band.Height = Math.Max(50 * scale, label.ActualHeight + label.Margin.Bottom);
        }
        public bool IsBusy { set { busy = value; UpdateEnabled(); } }
        private void UpdateEnabled() { PlayMeButton.IsEnabled = played || !busy; CreateMixButton.IsEnabled = !played || !busy; }
        public void SetHistory(IList<Album> albums)
        {
            SetRecent(RecentAlbumOne, RecentImageOne, albums, 0); SetRecent(RecentAlbumTwo, RecentImageTwo, albums, 1); SetRecent(RecentAlbumThree, RecentImageThree, albums, 2);
        }
        public void SetMixes(IList<Playlist> mixes)
        {
            SetMix(MixOneButton, MixOneImage, MixOneLabel, MixOneBand, mixes, 0, "mixes", "UiMixes", "MixesTileIcon.png");
            SetMix(MixTwoButton, MixTwoImage, MixTwoLabel, MixTwoBand, mixes, 1, "offline", "UiOfflineMixes", "OfflineTileIcon.png");
        }
        private static void SetMix(Button button, Image image, TextBlock label, FrameworkElement band, IList<Playlist> mixes, int index, string route, string resource, string icon)
        {
            var mix = index < mixes.Count ? mixes[index] : null;
            button.Tag = (object)mix ?? route;
            label.Text = mix == null ? PhoneText.Get(resource) : mix.Name;
            band.Visibility = mix == null ? Visibility.Collapsed : Visibility.Visible;
            SetImage(image, mix == null ? "/Assets/MixRadio/" + icon : mix.CoverArt);
        }
        public void SetCurrent(TrackInfo track, string elapsed)
        {
            var modeChanged = played != (track != null);
            played = track != null;
            UpdateEnabled();
            PlayMeButton.Tag = played ? "player" : "playme";
            PlayMeImage.Visibility = PlayMeLabel.Visibility = played ? Visibility.Collapsed : Visibility.Visible;
            CurrentImage.Visibility = CurrentShade.Visibility = CurrentClip.Visibility = played ? Visibility.Visible : Visibility.Collapsed;
            CurrentElapsed.Text = elapsed ?? string.Empty;
            CurrentTitle.Text = track == null ? string.Empty : track.Title;
            if (track != null && artwork != track.CoverArt) { artwork = track.CoverArt; CurrentTitle.Visibility = Visibility.Collapsed; SetImage(CurrentImage, artwork); }
            if (!played) CurrentTitle.Visibility = Visibility.Collapsed;
            CreateMixButton.Tag = played ? "playme" : "create";
            CreateMixButton.Background = (Brush)Application.Current.Resources[played ? "MixRadioPlayMeBrush" : "MixRadioAccentBrush"];
            CreateMixLabel.Text = PhoneText.Get(played ? "UiPlayMe" : "UiCreateMix");
            if (modeChanged) SetImage(CreateMixImage, "/Assets/MixRadio/" + (played ? "PlayMeTileIcon.png" : "CreateTileIcon.png"));
            AutomationProperties.SetName(PlayMeButton, played ? PhoneText.Get("UiNowPlaying") : PhoneText.Get("UiPlayMe"));
        }
        private void OnCurrentImageFailed(object sender, ExceptionRoutedEventArgs args) { if (played) CurrentTitle.Visibility = Visibility.Visible; }
        private static void SetRecent(Button button, Image image, IList<Album> albums, int index)
        {
            var album = index < albums.Count ? albums[index] : null;
            button.Tag = album; button.IsEnabled = album != null;
            SetImage(image, album == null ? null : album.CoverArt);
            AutomationProperties.SetName(button, album == null ? string.Empty : PhoneText.Format("OpenAlbum", album.Name, album.Artist));
        }
        private static void SetImage(Image image, string source)
        {
            image.Source = null;
            if (!string.IsNullOrEmpty(source)) image.Source = new BitmapImage(new Uri(source.StartsWith("/") ? "ms-appx://" + source : source));
        }
        private void OnTileClick(object sender, RoutedEventArgs args)
        {
            var tag = ((Button)sender).Tag;
            Raise(new PhoneTileEventArgs { Route = tag is Playlist ? "playlist" : tag as string, Playlist = tag as Playlist });
        }
        private void OnRecentClick(object sender, RoutedEventArgs args) { Raise(new PhoneTileEventArgs { Route = "album", Album = ((Button)sender).Tag as Album }); }
        private void Raise(PhoneTileEventArgs args) { var handler = TileClicked; if (handler != null) handler(this, args); }
    }
}
