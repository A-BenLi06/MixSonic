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
        public PhoneHomePanel() { InitializeComponent(); }
        public bool IsBusy { set { PlayMeButton.IsEnabled = !value; } }
        public void SetHistory(IList<Album> albums)
        {
            SetRecent(RecentAlbumOne, RecentImageOne, RecentTitleOne, albums, 0);
            SetRecent(RecentAlbumTwo, RecentImageTwo, RecentTitleTwo, albums, 1);
            SetRecent(RecentAlbumThree, RecentImageThree, RecentTitleThree, albums, 2);
        }
        private void SetRecent(Button button, Image image, TextBlock title, IList<Album> albums, int index)
        {
            var album = index < albums.Count ? albums[index] : null;
            button.Tag = album;
            button.IsEnabled = album != null;
            title.Text = album == null ? string.Empty : album.Name;
            image.Source = null;
            if (album == null) return;
            var source = album.CoverArt;
            if (!string.IsNullOrEmpty(source))
                image.Source = new BitmapImage(new Uri(source.StartsWith("/") ? "ms-appx://" + source : source));
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(button, PhoneText.Format("OpenAlbum", album.Name, album.Artist));
        }
        private void OnTileClick(object sender, RoutedEventArgs e) { Raise(new PhoneTileEventArgs { Route = ((Button)sender).Tag as string }); }
        private void OnRecentClick(object sender, RoutedEventArgs e) { Raise(new PhoneTileEventArgs { Route = "album", Album = ((Button)sender).Tag as Album }); }
        private void Raise(PhoneTileEventArgs args) { var handler = TileClicked; if (handler != null) handler(this, args); }
    }
}
