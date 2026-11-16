namespace Mixdrome.Navidrome
{
    using System;
    using Windows.System;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;

    public sealed partial class MixRadioPlayerSurface
    {
        public event EventHandler QueueRequested;
        public event EventHandler BrowseRequested;
        private NavidromePlayerViewModel Model { get { return DataContext as NavidromePlayerViewModel; } }
        private NavidromePlayerViewModel _scrubModel;
        public MixRadioPlayerSurface()
        {
            InitializeComponent();
            SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnScrubStarted), true);
            SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnScrubFinished), true);
            SeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnScrubFinished), true);
            SeekSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler((sender, args) => FinishScrub(false)), true);
            SeekSlider.AddHandler(KeyDownEvent, new KeyEventHandler((sender, args) => { if (IsSeekKey(args.Key)) BeginScrub(); }), true);
            SeekSlider.AddHandler(KeyUpEvent, new KeyEventHandler((sender, args) => { if (IsSeekKey(args.Key)) FinishScrub(); }), true);
            SeekSlider.LostFocus += (sender, args) => FinishScrub();
            Unloaded += (sender, args) => FinishScrub(false);
            DataContextChanged += (sender, args) => FinishScrub(false);
        }
        public void SetViewport(double width, double height)
        {
            var narrow = width < 800;
            var artwork = Math.Min(344, Math.Max(270, width - 40));
            Surface.Width = narrow ? artwork : 1120;
            PlayerContent.Margin = new Thickness(narrow ? 0 : 80, 0, 0, 0);
            PlayerScroll.MaxHeight = narrow ? double.PositiveInfinity : Math.Max(240, height - 190);
            for (var index = 0; index < PlayerLayout.ColumnDefinitions.Count; index++)
                PlayerLayout.ColumnDefinitions[index].Width = new GridLength(narrow ? (index < 2 ? artwork / 2 : 0) : index == 4 ? 344 : 172);
            PlayerLayout.RowDefinitions[0].Height = new GridLength(narrow ? artwork : 172);
            PlayerLayout.RowDefinitions[1].Height = narrow ? GridLength.Auto : new GridLength(172);
            PlayerLayout.RowDefinitions[2].Height = GridLength.Auto;
            foreach (var element in new FrameworkElement[] { ArtworkButton, EmptyArtwork }) Grid.SetRowSpan(element, narrow ? 1 : 2);
            foreach (var element in new FrameworkElement[] { TrackInformation, QuietPanel })
            { Grid.SetColumn(element, narrow ? 0 : 2); Grid.SetRow(element, narrow ? 1 : 0); Grid.SetColumnSpan(element, narrow ? 2 : 3); Grid.SetRowSpan(element, narrow ? 1 : 2); }
            TrackInformation.Margin = narrow ? new Thickness(0, 0, 0, 12) : new Thickness(8, 0, 0, 0);
            QuietPanel.Margin = narrow ? new Thickness(0, 16, 0, 16) : new Thickness(10, 0, 400, 0);
            foreach (var button in new[] { PreviousButton, PlayButton, PauseButton, NextButton })
                button.Margin = narrow ? new Thickness(0) : new Thickness(0, 0, 23, 0);
            TrackTitle.FontSize = narrow ? 26.667 : 38;
            ArtistName.MaxWidth = narrow ? artwork - 40 : 650;
            Grid.SetColumn(StreamStatus, narrow ? 0 : 2); Grid.SetRow(StreamStatus, narrow ? 3 : 2);
            Grid.SetColumnSpan(StreamStatus, narrow ? 2 : 3);
            StreamStatus.Margin = narrow ? new Thickness(0, 0, 0, 20) : new Thickness(8, 12, 32, 0);
        }
        private static bool IsSeekKey(VirtualKey key)
        { return key == VirtualKey.Left || key == VirtualKey.Right || key == VirtualKey.Up || key == VirtualKey.Down || key == VirtualKey.Home || key == VirtualKey.End || key == VirtualKey.PageUp || key == VirtualKey.PageDown; }
        private void BeginScrub()
        {
            if (_scrubModel != null || Model == null) return;
            Model.BeginScrub();
            if (Model.IsScrubbing) _scrubModel = Model;
        }
        private void OnScrubStarted(object sender, PointerRoutedEventArgs args) { BeginScrub(); }
        private void OnScrubFinished(object sender, PointerRoutedEventArgs args) { FinishScrub(); }
        private void FinishScrub(bool commit = true)
        {
            var model = _scrubModel;
            _scrubModel = null;
            if (model != null) model.FinishScrub(commit);
        }
        private void OnPreviousClicked(object sender, RoutedEventArgs args) { if (Model != null) Model.Previous(); }
        private void OnPlayPauseClicked(object sender, RoutedEventArgs args) { if (Model != null) Model.PlayPause(); }
        private void OnNextClicked(object sender, RoutedEventArgs args) { if (Model != null) Model.Next(); }
        private void OnDetailsClicked(object sender, RoutedEventArgs args) { if (Model != null) Model.Details(); }
        private void OnArtistClicked(object sender, RoutedEventArgs args) { if (Model != null) Model.OpenArtist(); }
        private async void OnFavouriteClicked(object sender, RoutedEventArgs args) { if (Model != null) await Model.Favourite(); }
        private void OnQueueClicked(object sender, RoutedEventArgs args) { if (QueueRequested != null) QueueRequested(this, EventArgs.Empty); }
        private void OnBrowseClicked(object sender, RoutedEventArgs args) { if (BrowseRequested != null) BrowseRequested(this, EventArgs.Empty); }
    }
}
