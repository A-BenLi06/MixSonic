namespace Subsonic8.Navidrome
{
    using System;
    using System.ComponentModel;
    using Caliburn.Micro;
    using Microsoft.Practices.ServiceLocation;
    using Windows.System;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;

    public sealed partial class NavidromePlayerView
    {
        private NavidromePlayerViewModel _model;
        private MixRadioPlayerSurface _surface;
        private ListView _lyrics;
        private FrameworkElement _lyricsPanel;
        private bool _loaded;
        public NavidromePlayerView()
        {
            InitializeComponent();
            SizeChanged += (sender, args) => Reflow();
            Loaded += (sender, args) => { _loaded = true; Attach(); Reflow(); };
            Unloaded += (sender, args) =>
            {
                _loaded = false;
                if (_model != null) _model.PropertyChanged -= ModelChanged;
                _model = null; _surface = null; _lyrics = null; _lyricsPanel = null;
            };
            DataContextChanged += (sender, args) => Attach();
        }
        private void OnPlayerLoaded(object sender, RoutedEventArgs args) { _surface = (MixRadioPlayerSurface)sender; Reflow(); }
        private void OnLyricsPanelLoaded(object sender, RoutedEventArgs args) { _lyricsPanel = (FrameworkElement)sender; Reflow(); }
        private void OnLyricsLoaded(object sender, RoutedEventArgs args)
        {
            var list = (ListView)sender;
            if (!ReferenceEquals(_lyrics, list))
            {
                list.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(PauseLyricsFollow), true);
                list.AddHandler(PointerPressedEvent, new PointerEventHandler(PauseLyricsFollow), true);
            }
            _lyrics = list; Reflow();
        }
        private void PauseLyricsFollow(object sender, PointerRoutedEventArgs args) { if (_model != null) _model.FollowLyrics = false; }
        private void Attach()
        {
            if (_model != null) _model.PropertyChanged -= ModelChanged;
            if (!_loaded) return;
            _model = DataContext as NavidromePlayerViewModel;
            if (_model != null) _model.PropertyChanged += ModelChanged;
        }
        private void ModelChanged(object sender, PropertyChangedEventArgs args)
        {
            if (!_loaded || _lyrics == null || _model == null || args.PropertyName != "HighlightedLine" || !_model.FollowLyrics) return;
            var index = _model.HighlightedLine;
            if (index >= 0 && index < _model.LyricLines.Count) _lyrics.ScrollIntoView(_model.LyricLines[index], ScrollIntoViewAlignment.Leading);
        }
        private void Reflow()
        {
            PlayerHub.Orientation = ActualWidth < 800 ? Orientation.Vertical : Orientation.Horizontal;
            if (_surface != null) _surface.SetViewport(ActualWidth, ActualHeight);
            if (_lyricsPanel != null) _lyricsPanel.Width = Math.Min(340, Math.Max(270, ActualWidth - 40));
            if (_lyrics != null) _lyrics.MaxHeight = Math.Max(200, ActualHeight - 300);
        }
        private void OnQueueRequested(object sender, EventArgs args)
        { QueueDismiss.Visibility = QueueDrawer.Visibility = Visibility.Visible; QueueDrawer.FocusCloseButton(); }
        private void OnQueueClosed(object sender, EventArgs args) { CloseQueue(); }
        private void OnQueueDismissed(object sender, RoutedEventArgs args) { CloseQueue(); }
        private void CloseQueue() { QueueDismiss.Visibility = QueueDrawer.Visibility = Visibility.Collapsed; }
        private void OnBrowseRequested(object sender, EventArgs args)
        { ServiceLocator.Current.GetInstance<INavigationService>().NavigateToViewModel<NavidromeBrowserViewModel>("albums"); }
        private void OnPageKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key != VirtualKey.Escape || QueueDrawer.Visibility != Visibility.Visible) return;
            CloseQueue(); args.Handled = true;
        }
    }
}
