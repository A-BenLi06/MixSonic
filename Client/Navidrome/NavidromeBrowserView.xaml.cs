namespace Subsonic8.Navidrome
{
    using System;
    using System.Linq;
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
            GoBack.Margin = narrow ? new Thickness(inset, 0, 0, 36) : new Thickness(36, 0, 36, 36);
            PageTitle.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            FilterGap.Width = new GridLength(narrow ? 0 : 54);
            HeaderSearchWidth.Width = new GridLength(searchBelowTitle ? 20 : 318);
            CurrentFilterText.FontSize = narrow ? 32 : 40;
            HeaderHeight.Height = new GridLength(searchBelowTitle ? 162 : 134);
            SearchControl.Width = Math.Min(270, Math.Max(220, width - 40));
            SearchControl.Margin = searchBelowTitle ? new Thickness(inset, 112, 20, 0) : new Thickness(0, 70, 32, 0);
            SearchControl.HorizontalAlignment = searchBelowTitle ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            // Align the recovered container's content with the reference's
            // first card at (130,149), retaining its 307 x 147 cell spacing.
            LibraryItems.Padding = new Thickness(inset + 3, 3, narrow ? 0 : 40, 16);
            Feedback.Margin = new Thickness(inset, 0, 40, 0);
            EmptyMessage.Margin = new Thickness(inset, 0, 20, 0);
            LoadMore.Margin = new Thickness(inset, 12, 20, 20);
            var wrap = LibraryItems.ItemsPanelRoot as WrapGrid;
            if (wrap != null) wrap.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
            ScrollViewer.SetHorizontalScrollMode(LibraryItems, narrow ? ScrollMode.Disabled : ScrollMode.Enabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(LibraryItems, narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollMode(LibraryItems, narrow ? ScrollMode.Enabled : ScrollMode.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(LibraryItems, narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        }
        private void OnLibraryLoaded(object sender, RoutedEventArgs args) { Reflow(ActualWidth); }
        private async void OnItemClicked(object sender, ItemClickEventArgs args)
        { var model = DataContext as NavidromeBrowserViewModel; if (model != null) await model.OpenItem(args); }
        private void OnLibraryClicked(object sender, RoutedEventArgs args)
        {
            var destinations = LibraryDestination.All();
            var model = DataContext as NavidromeBrowserViewModel;
            var route = model == null ? null : NavidromeRoute.Parse(model.Parameter)[0];
            var choices = new ListBox
            {
                ItemsSource = destinations, DisplayMemberPath = "Label",
                ItemContainerStyle = (Style)Application.Current.Resources["FilterListItemStyle"],
                SelectedItem = destinations.FirstOrDefault(destination => destination.Route == route),
                Width = Math.Min(300, Math.Max(220, ActualWidth - 40)),
                MaxHeight = Math.Max(120, ActualHeight - 180),
                BorderThickness = new Thickness(0), Padding = new Thickness(0)
            };
            Windows.UI.Xaml.Automation.AutomationProperties.SetName(choices, model == null ? string.Empty : model.DisplayName);
            var menu = new Flyout { Content = choices, FlyoutPresenterStyle = (Style)Resources["MixRadioLibraryMenuStyle"] };
            choices.SelectionChanged += (list, changed) =>
            {
                var target = choices.SelectedItem as LibraryDestination;
                if (target == null) return;
                menu.Hide();
                target.Navigate(ServiceLocator.Current.GetInstance<INavigationService>());
            };
            menu.ShowAt((FrameworkElement)sender);
        }
        private void OnPageKeyDown(object sender, KeyRoutedEventArgs args)
        { SearchControl.HandleShortcut(args); }
    }
}
