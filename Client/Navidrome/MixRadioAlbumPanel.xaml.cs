namespace Mixdrome.Navidrome
{
    using System;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    public sealed partial class MixRadioAlbumPanel
    {
        public event EventHandler CloseRequested;
        private NavidromeDetailViewModel Model { get { return DataContext as NavidromeDetailViewModel; } }
        public MixRadioAlbumPanel() { InitializeComponent(); SizeChanged += (sender, args) => Reflow(); }
        public void FocusCloseButton() { CloseButton.Focus(FocusState.Programmatic); }
        private void Reflow()
        {
            var compact = ActualWidth < 760;
            OverviewWidth.Width = new GridLength(compact ? 1 : 326, compact ? GridUnitType.Star : GridUnitType.Pixel);
            OverviewHeight.Height = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            TracksHeight.Height = new GridLength(compact ? 1 : 0, compact ? GridUnitType.Star : GridUnitType.Pixel);
            Grid.SetColumn(TrackRegion, compact ? 0 : 1); Grid.SetRow(TrackRegion, compact ? 1 : 0);
            Grid.SetColumnSpan(Overview, compact ? 2 : 1); Grid.SetColumnSpan(TrackRegion, compact ? 2 : 1);
            Grid.SetColumn(Description, compact ? 1 : 0); Grid.SetRow(Description, compact ? 0 : 1);
            Overview.Margin = compact ? new Thickness(24, 34, 24, 20) : new Thickness(34, 34, 0, 24);
            TrackRegion.Margin = compact ? new Thickness(12, 0, 0, 0) : new Thickness(0, 34, 0, 0);
            CoverWidth.Width = new GridLength(compact ? 128 : 250);
            DescriptionWidth.Width = new GridLength(compact ? 1 : 0, compact ? GridUnitType.Star : GridUnitType.Pixel);
            Cover.Width = Cover.Height = compact ? 128 : 250;
            Description.Width = compact ? double.NaN : 250;
            Description.Margin = compact ? new Thickness(20, 0, 0, 0) : new Thickness(0);
            AlbumTitle.FontSize = compact ? 22 : 26.667; AlbumTitle.Margin = new Thickness(0, compact ? 0 : 25, 0, 0);
            ArtistName.FontSize = compact ? 18 : 20;
            SongsList.Padding = new Thickness(0, 0, compact ? 24 : 70, 0);
        }
        private async void OnPlayClicked(object sender, RoutedEventArgs args) { if (Model != null && Model.CanPlay) await Model.Play(); }
        private void OnTrackClicked(object sender, ItemClickEventArgs args) { if (Model != null) Model.PlayTrack(args); }
        private void OnArtistClicked(object sender, RoutedEventArgs args) { if (Model != null) Model.OpenAlbumArtist(); }
        private void OnDetailsClicked(object sender, RoutedEventArgs args) { if (Model != null) Model.OpenFullDetails(); }
        private void OnCloseClicked(object sender, RoutedEventArgs args) { if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); }
    }
}
