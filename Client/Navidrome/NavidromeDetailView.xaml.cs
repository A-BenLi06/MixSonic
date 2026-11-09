namespace Subsonic8.Navidrome
{
    using System;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    public sealed partial class NavidromeDetailView
    {
        private ScrollViewer _overview;
        private GridView _tracks, _related;
        public NavidromeDetailView()
        {
            InitializeComponent();
            SizeChanged += (sender, args) => Reflow();
            Loaded += (sender, args) => Reflow();
            Unloaded += (sender, args) => { _overview = null; _tracks = null; _related = null; };
        }
        private void Reflow()
        {
            var narrow = ActualWidth < 800;
            DetailHub.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
            PageTitle.FontSize = narrow ? 32 : 56;
            PageTitle.MaxWidth = Math.Max(220, ActualWidth - 160);
            OverviewSection.Margin = new Thickness(narrow ? 0 : 80, 0, 17, 0);
            if (_overview != null)
            {
                _overview.Width = Math.Min(360, Math.Max(280, ActualWidth - 40));
                _overview.MaxHeight = narrow ? double.PositiveInfinity : Math.Max(220, ActualHeight - 190);
                var stack = _overview.Content as StackPanel;
                if (stack != null && stack.Children.Count > 0)
                { var artwork = stack.Children[0] as FrameworkElement; if (artwork != null) artwork.Width = artwork.Height = Math.Min(334, _overview.Width); }
            }
            foreach (var list in new[] { _tracks, _related })
            {
                if (list == null) continue;
                list.Width = narrow ? Math.Max(304, ActualWidth - 32) : ReferenceEquals(list, _tracks) ? 620 : 510;
                list.Height = Math.Max(240, ActualHeight - 220);
                var wrap = list.ItemsPanelRoot as WrapGrid;
                if (wrap != null) wrap.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
                ScrollViewer.SetHorizontalScrollMode(list, narrow ? ScrollMode.Disabled : ScrollMode.Enabled);
                ScrollViewer.SetVerticalScrollMode(list, narrow ? ScrollMode.Enabled : ScrollMode.Disabled);
            }
        }
        private void OnOverviewLoaded(object sender, RoutedEventArgs args) { _overview = (ScrollViewer)sender; Reflow(); }
        private void OnTracksLoaded(object sender, RoutedEventArgs args) { _tracks = (GridView)sender; Reflow(); }
        private void OnRelatedLoaded(object sender, RoutedEventArgs args) { _related = (GridView)sender; Reflow(); }
        private void OnTrackClicked(object sender, ItemClickEventArgs args)
        { var model = DataContext as NavidromeDetailViewModel; if (model != null) model.PlayTrack(args); }
        private void OnRelatedClicked(object sender, ItemClickEventArgs args)
        { var model = DataContext as NavidromeDetailViewModel; if (model != null) model.OpenRelated(args); }
    }
}
