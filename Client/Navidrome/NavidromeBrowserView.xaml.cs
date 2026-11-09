namespace Subsonic8.Navidrome
{
    using System;
    using Caliburn.Micro;
    using Microsoft.Practices.ServiceLocation;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;

    public sealed partial class NavidromeBrowserView
    {
        public NavidromeBrowserView()
        {
            InitializeComponent();
            SizeChanged += (sender, args) => Reflow(args.NewSize.Width);
            Loaded += (sender, args) => Reflow(ActualWidth);
        }
        private void Reflow(double width)
        {
            var narrow = width < 800;
            var searchBelowTitle = width < 1200;
            var inset = width < 500 ? 12 : narrow ? 20 : 116;
            HeaderIndent.Width = new GridLength(narrow ? 68 : 116);
            PageTitle.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            CurrentFilterText.MaxWidth = Math.Max(150, width - (narrow ? 120 : searchBelowTitle ? 530 : 830));
            CurrentFilterText.FontSize = narrow ? 32 : 40;
            HeaderHeight.Height = new GridLength(searchBelowTitle ? 162 : 134);
            SearchControl.Width = Math.Min(270, Math.Max(220, width - 40));
            SearchControl.Margin = searchBelowTitle ? new Thickness(inset, 112, 20, 0) : new Thickness(0, 70, 32, 0);
            SearchControl.HorizontalAlignment = searchBelowTitle ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            LibraryItems.Padding = new Thickness(inset, 0, narrow ? 0 : 40, 20);
            Feedback.Margin = new Thickness(inset, 0, 40, 12);
            EmptyMessage.Margin = new Thickness(inset, 0, 20, 0);
            LoadMore.Margin = new Thickness(inset, 12, 20, 20);
            var wrap = LibraryItems.ItemsPanelRoot as WrapGrid;
            if (wrap != null) wrap.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
            ScrollViewer.SetHorizontalScrollMode(LibraryItems, narrow ? ScrollMode.Disabled : ScrollMode.Enabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(LibraryItems, narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollMode(LibraryItems, narrow ? ScrollMode.Enabled : ScrollMode.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(LibraryItems, narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        }
        private async void OnItemClicked(object sender, ItemClickEventArgs args)
        { var model = DataContext as NavidromeBrowserViewModel; if (model != null) await model.OpenItem(args); }
        private void OnLibraryClicked(object sender, RoutedEventArgs args)
        {
            var menu = new MenuFlyout();
            foreach (var destination in LibraryDestination.All())
            {
                var target = destination;
                var item = new MenuFlyoutItem { Text = target.Label };
                item.Click += (button, clicked) => target.Navigate(ServiceLocator.Current.GetInstance<INavigationService>());
                menu.Items.Add(item);
            }
            menu.ShowAt((FrameworkElement)sender);
        }
        private void OnPageKeyDown(object sender, KeyRoutedEventArgs args)
        { SearchControl.HandleShortcut(args); }
    }
}
