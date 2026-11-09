namespace Subsonic8.Navidrome
{
    using System.ComponentModel;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;

    public sealed partial class NavidromePlayerView
    {
        private NavidromePlayerViewModel _model;
        private bool _loaded;
        public NavidromePlayerView()
        {
            InitializeComponent();
            SizeChanged += (sender, args) => Reflow(args.NewSize.Width);
            Loaded += (sender, args) => { _loaded = true; Attach(); Reflow(ActualWidth); };
            Unloaded += (sender, args) => { _loaded = false; if (_model != null) _model.PropertyChanged -= ModelChanged; _model = null; };
            DataContextChanged += (sender, args) => Attach();
            SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler((sender, args) => { if (_model != null) _model.IsScrubbing = true; }), true);
            SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(EndSeek), true);
            SeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(EndSeek), true);
            LyricsList.AddHandler(PointerWheelChangedEvent, new PointerEventHandler((sender, args) => { if (_model != null) _model.FollowLyrics = false; }), true);
            LyricsList.AddHandler(PointerPressedEvent, new PointerEventHandler((sender, args) => { if (_model != null) _model.FollowLyrics = false; }), true);
        }
        private void EndSeek(object sender, PointerRoutedEventArgs args) { if (_model != null) { _model.IsScrubbing = false; _model.UpdateTiming(); } }
        private void Attach()
        {
            if (_model != null) _model.PropertyChanged -= ModelChanged;
            if (!_loaded) return;
            _model = DataContext as NavidromePlayerViewModel;
            if (_model != null) _model.PropertyChanged += ModelChanged;
        }
        private void ModelChanged(object sender, PropertyChangedEventArgs args)
        {
            if (!_loaded || _model == null || args.PropertyName != "HighlightedLine" || !_model.FollowLyrics) return;
            var index = _model.HighlightedLine;
            if (index >= 0 && index < _model.LyricLines.Count) LyricsList.ScrollIntoView(_model.LyricLines[index], ScrollIntoViewAlignment.Leading);
        }
        private void Reflow(double width)
        {
            var narrow = width < 700; var medium = width < 1100;
            Canvas.Margin = narrow ? new Thickness(20, 24, 20, 20) : new Thickness(88, 30, 48, 40);
            PageTitle.FontSize = width < 500 ? 32 : 44;
            PlayerColumn.Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(medium ? 320 : 360);
            QueueColumn.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            LyricsColumn.Width = medium ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(QueuePanel, medium ? 0 : 1); Grid.SetRow(QueuePanel, medium ? 1 : 0); Grid.SetColumnSpan(QueuePanel, !narrow && medium ? 2 : 1);
            Grid.SetColumn(LyricsPanel, narrow ? 0 : medium ? 1 : 2); Grid.SetRow(LyricsPanel, narrow ? 2 : 0);
            QueuePanel.Margin = medium ? new Thickness(0, 28, 0, 0) : new Thickness(20, 0, 20, 0);
            LyricsPanel.Margin = narrow ? new Thickness(0, 28, 0, 0) : new Thickness(20, 0, 0, 0);
            ArtworkImage.Width = ArtworkImage.Height = narrow ? 220 : medium ? 280 : 320;
        }
    }
}
