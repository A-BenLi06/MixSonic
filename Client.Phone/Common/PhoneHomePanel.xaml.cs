namespace Mixdrome.Phone.Common
{
    using System;
    using System.Collections.Generic;
    using Client.Common.Models.Subsonic;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media.Imaging;
    public sealed class PhoneTileEventArgs : EventArgs
    {
        public string Route { get; set; }
        public Album Album { get; set; }
    }
    public sealed partial class PhoneHomePanel : UserControl
    {
        public event EventHandler<PhoneTileEventArgs> TileClicked;
        public PhoneHomePanel()
        {
            InitializeComponent();
            SizeChanged += OnPanelSizeChanged;
        }

        private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
        {
            var width = e.NewSize.Width;
            if (width <= 0 || double.IsInfinity(width) || double.IsNaN(width)) return;
            var resources = Application.Current.Resources;
            var large = (double)resources["PhoneHomeLargeTileSize"];
            var standard = (double)resources["PhoneHomeStandardTileSize"];
            var recent = (double)resources["PhoneHomeRecentTileSize"];
            var gap = (double)resources["PhoneHomeTileGap"];
            var offset = (double)resources["PhoneHomeSecondColumnOffset"];
            // Geometry adapts to the HubSection viewport; labels remain native
            // TextBlocks rather than becoming scaled content in a fixed canvas.
            var scale = Math.Min(1, width / (large + standard + gap * 2));
            gap *= scale;
            LargeTileColumn.Width = new GridLength((large * scale) + gap);
            SmallTileColumn.Width = new GridLength((standard * scale) + gap);
            OffsetColumn.Margin = new Thickness(0, offset * scale, 0, 0);
            SizeTile(PlayMeButton, large * scale, gap);
            SizeTile(MixesButton, standard * scale, gap);
            SizeTile(MyMusicButton, standard * scale, gap);
            SizeTile(FavouritesButton, standard * scale, gap);
            SizeTile(RecentTileOne, recent * scale, gap);
            SizeTile(RecentTileTwo, recent * scale, gap);
            SizeTile(RecentTileThree, recent * scale, gap);
        }

        private static void SizeTile(FrameworkElement tile, double size, double gap)
        {
            tile.Width = tile.Height = size;
            tile.Margin = new Thickness(0, 0, gap, gap);
        }
        public bool IsBusy { set { PlayMeButton.IsEnabled = !value; } }
        public void SetHistory(IList<Album> albums)
        {
            SetRecent(RecentAlbumOne, RecentImageOne, RecentTitleOne, RecentPlaceholderOne, albums, 0);
            SetRecent(RecentAlbumTwo, RecentImageTwo, RecentTitleTwo, RecentPlaceholderTwo, albums, 1);
            SetRecent(RecentAlbumThree, RecentImageThree, RecentTitleThree, RecentPlaceholderThree, albums, 2);
        }
        private void SetRecent(Button button, Image image, TextBlock title, FrameworkElement placeholder, IList<Album> albums, int index)
        {
            var album = index < albums.Count ? albums[index] : null;
            button.Tag = album;
            button.IsEnabled = album != null;
            button.Visibility = album == null ? Visibility.Collapsed : Visibility.Visible;
            placeholder.Visibility = album == null ? Visibility.Visible : Visibility.Collapsed;
            title.Text = album == null ? string.Empty : album.Name;
            image.Source = null;
            title.Visibility = Visibility.Collapsed;
            if (album == null)
            {
                Windows.UI.Xaml.Automation.AutomationProperties.SetName(button, string.Empty);
                return;
            }
            var source = album.CoverArt;
            if (!string.IsNullOrEmpty(source))
                image.Source = new BitmapImage(new Uri(source.StartsWith("/") ? "ms-appx://" + source : source));
            else title.Visibility = Visibility.Visible;
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(button, PhoneText.Format("OpenAlbum", album.Name, album.Artist));
        }
        private void OnTileClick(object sender, RoutedEventArgs e) { Raise(new PhoneTileEventArgs { Route = ((Button)sender).Tag as string }); }
        private void OnRecentClick(object sender, RoutedEventArgs e) { Raise(new PhoneTileEventArgs { Route = "album", Album = ((Button)sender).Tag as Album }); }
        private void Raise(PhoneTileEventArgs args) { var handler = TileClicked; if (handler != null) handler(this, args); }
    }
}
