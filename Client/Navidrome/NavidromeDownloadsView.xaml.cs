namespace Mixdrome.Navidrome
{
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    public sealed partial class NavidromeDownloadsView
    {
        public NavidromeDownloadsView()
        {
            InitializeComponent();
            SizeChanged += (sender, args) => Reflow();
            Loaded += (sender, args) => Reflow();
        }
        private void Reflow()
        {
            var narrow = ActualWidth < 800;
            var inset = narrow ? 20 : 116;
            HeaderIndent.Width = new GridLength(narrow ? 68 : 116);
            PageTitle.FontSize = narrow ? 32 : 56;
            Feedback.Margin = new Thickness(inset, 0, 20, 20);
            EmptyMessage.Margin = new Thickness(inset, 0, 20, 0);
            DownloadsList.Padding = new Thickness(inset, 0, 20, 20);
            var panel = DownloadsList.ItemsPanelRoot as WrapGrid;
            if (panel != null) panel.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
            ScrollViewer.SetHorizontalScrollMode(DownloadsList, narrow ? ScrollMode.Disabled : ScrollMode.Enabled);
            ScrollViewer.SetVerticalScrollMode(DownloadsList, narrow ? ScrollMode.Enabled : ScrollMode.Disabled);
        }
        private async void OnItemClicked(object sender, ItemClickEventArgs args)
        {
            var model = DataContext as NavidromeDownloadsViewModel;
            var item = args.ClickedItem as OfflineDownloadItem;
            if (model == null || item == null) return;
            if (item.CanPlay) await model.OpenItem(args);
            else { DownloadsList.SelectedItem = item; BottomAppBar.IsOpen = true; }
        }
    }
}
