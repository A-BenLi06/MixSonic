namespace Subsonic8.Navidrome
{
    using System;
    using System.ComponentModel;
    using System.Linq;
    using Caliburn.Micro;
    using Microsoft.Practices.ServiceLocation;
    using Windows.System;
    using Windows.UI.Core;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;
    using Windows.UI.Xaml.Media;

    public sealed partial class NavidromeBrowserView
    {
        private NavidromeBrowserViewModel _model;
        private LibraryIndexGroup _indexOrigin;
        private object _scrollAnchor;
        private bool _syncingSelection;
        public NavidromeBrowserView()
        {
            InitializeComponent();
            SizeChanged += (sender, args) => Reflow(args.NewSize.Width);
            Loaded += (sender, args) =>
            {
                _model = DataContext as NavidromeBrowserViewModel;
                if (_model != null) _model.PropertyChanged += OnModelChanged;
                Reflow(ActualWidth);
            };
            Unloaded += (sender, args) => { if (_model != null) _model.PropertyChanged -= OnModelChanged; _model = null; };
            AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnZoomWheel), true);
        }
        private void Reflow(double width)
        {
            var narrow = width < 800;
            var searchBelowTitle = width < 1200;
            var inset = width < 500 ? 12 : narrow ? 20 : 116;
            HeaderIndent.Width = new GridLength(narrow ? 68 : 116);
            LibraryBack.Margin = narrow ? new Thickness(inset, 0, 0, 36) : new Thickness(36, 0, 36, 36);
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
            if (wrap != null)
            {
                wrap.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
                // Recovered song columns cap at seven rows; album columns at
                // ten, otherwise using the available height.
                wrap.MaximumRowsOrColumns = narrow || _model == null || !_model.HasIndex ? -1 : NavidromeRoute.Parse(_model.Parameter)[0] == "songs" ? 7 : 10;
            }
            ScrollViewer.SetHorizontalScrollMode(LibraryItems, narrow ? ScrollMode.Disabled : ScrollMode.Enabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(LibraryItems, narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollMode(LibraryItems, narrow ? ScrollMode.Enabled : ScrollMode.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(LibraryItems, narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
            var named = _model != null && _model.IsNamedIndex;
            LibraryIndexItems.Padding = new Thickness(inset + 3, 0, narrow ? 12 : 40, 0);
            LibraryIndexItems.MaxHeight = Math.Max(120, ActualHeight - HeaderHeight.Height.Value - 32);
            LibraryIndexItems.RenderTransform = new TranslateTransform { Y = named && !narrow ? -88 : 0 };
            var indexWrap = LibraryIndexItems.ItemsPanelRoot as WrapGrid;
            if (indexWrap != null)
            {
                indexWrap.Orientation = named && !narrow ? Orientation.Vertical : Orientation.Horizontal;
                indexWrap.MaximumRowsOrColumns = named && !narrow ? 1 : -1;
            }
            ScrollViewer.SetHorizontalScrollMode(LibraryIndexItems, named && !narrow ? ScrollMode.Enabled : ScrollMode.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(LibraryIndexItems, named && !narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollMode(LibraryIndexItems, named && !narrow ? ScrollMode.Disabled : ScrollMode.Enabled);
            ScrollViewer.SetVerticalScrollBarVisibility(LibraryIndexItems, named && !narrow ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
        }
        private void OnLibraryLoaded(object sender, RoutedEventArgs args) { Reflow(ActualWidth); }
        private async void OnItemClicked(object sender, ItemClickEventArgs args)
        {
            var group = args.ClickedItem as LibraryIndexGroup;
            if (group != null) { await ShowIndex(group); return; }
            var model = DataContext as NavidromeBrowserViewModel; if (model != null) await model.OpenItem(args);
        }
        private void OnLibrarySelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (_syncingSelection) return;
            var model = DataContext as NavidromeBrowserViewModel;
            if (model == null || model.IsUpdatingLibrary) return;
            _syncingSelection = true;
            try
            {
                foreach (var header in LibraryItems.SelectedItems.OfType<LibraryIndexGroup>().ToList()) LibraryItems.SelectedItems.Remove(header);
                var selected = LibraryItems.SelectedItems.OfType<NavidromeItem>().ToList();
                foreach (var removed in model.SelectedItems.Where(item => !selected.Contains(item)).ToList()) model.SelectedItems.Remove(removed);
                foreach (var item in selected.Where(item => !model.SelectedItems.Contains(item))) model.SelectedItems.Add(item);
            }
            finally { _syncingSelection = false; }
        }
        private void OnModelChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == "IsLoading" && _model.IsLoading) CloseIndex(false);
            if (args.PropertyName == "IsNamedIndex" || args.PropertyName == "HasIndex") Reflow(ActualWidth);
            if (args.PropertyName == "IsUpdatingLibrary" && _model.IsUpdatingLibrary) _scrollAnchor = FirstVisibleEntry();
            if (args.PropertyName == "IsUpdatingLibrary" && !_model.IsUpdatingLibrary)
            {
                _syncingSelection = true;
                try { foreach (var item in _model.SelectedItems) if (!LibraryItems.SelectedItems.Contains(item)) LibraryItems.SelectedItems.Add(item); }
                finally { _syncingSelection = false; }
                var header = _scrollAnchor as LibraryIndexGroup;
                if (header != null) _scrollAnchor = _model.IndexGroups.FirstOrDefault(group => group.Key == header.Key);
                if (_scrollAnchor != null && LibraryItems.Items.Contains(_scrollAnchor))
                { LibraryItems.UpdateLayout(); LibraryItems.ScrollIntoView(_scrollAnchor, ScrollIntoViewAlignment.Leading); }
            }
        }
        private async void OnShowIndex(object sender, RoutedEventArgs args) { await ShowIndex(); }
        private object FirstVisibleEntry()
        {
            for (var index = 0; index < LibraryItems.Items.Count; index++)
            {
                var item = LibraryItems.ContainerFromIndex(index) as FrameworkElement;
                if (item == null) continue;
                var point = item.TransformToVisual(LibraryItems).TransformPoint(new Windows.Foundation.Point());
                if (point.X >= 0 && point.Y >= 0 && point.X < LibraryItems.ActualWidth && point.Y < LibraryItems.ActualHeight)
                    return LibraryItems.Items[index];
            }
            return null;
        }
        internal async System.Threading.Tasks.Task ShowIndex(LibraryIndexGroup origin = null)
        {
            var model = DataContext as NavidromeBrowserViewModel;
            if (model == null) return;
            // Opening from the command bar retains the current scroll context.
            var entry = FirstVisibleEntry();
            _indexOrigin = origin ?? entry as LibraryIndexGroup ?? model.IndexGroups.FirstOrDefault(group => group.Items.Contains(entry as NavidromeItem));
            if (BottomAppBar != null) BottomAppBar.IsOpen = false;
            if (!await model.PrepareIndex() || !ReferenceEquals(model, DataContext)) return;
            if (_indexOrigin != null) _indexOrigin = model.IndexGroups.FirstOrDefault(group => group.Key == _indexOrigin.Key);
            IndexSurface.Visibility = Visibility.Visible;
            LibraryItems.Visibility = Visibility.Collapsed;
            LoadMore.Visibility = Visibility.Collapsed;
            LibraryIndexItems.UpdateLayout(); Reflow(ActualWidth);
            var target = _indexOrigin ?? model.IndexGroups.FirstOrDefault(group => group.IsEnabled);
            if (target != null)
            {
                LibraryIndexItems.ScrollIntoView(target);
                LibraryIndexItems.UpdateLayout();
                var container = LibraryIndexItems.ContainerFromItem(target) as Control;
                if (container != null) container.Focus(FocusState.Keyboard);
            }
        }
        private void OnIndexClicked(object sender, ItemClickEventArgs args) { JumpToIndex(args.ClickedItem as LibraryIndexGroup); }
        internal void JumpToIndex(LibraryIndexGroup group)
        {
            if (group == null || !group.IsEnabled) return;
            _indexOrigin = group;
            CloseIndex(true);
        }
        private void CloseIndex(bool restoreFocus)
        {
            if (IndexSurface.Visibility != Visibility.Visible) return;
            IndexSurface.Visibility = Visibility.Collapsed;
            LibraryItems.Visibility = Visibility.Visible;
            LoadMore.SetBinding(VisibilityProperty, new Windows.UI.Xaml.Data.Binding { Path = new PropertyPath("CanLoadMore"), Converter = (Windows.UI.Xaml.Data.IValueConverter)Resources["NavidromeVisibility"] });
            if (restoreFocus && _indexOrigin != null && LibraryItems.Items.Contains(_indexOrigin))
            {
                LibraryItems.UpdateLayout();
                LibraryItems.ScrollIntoView(_indexOrigin, ScrollIntoViewAlignment.Leading);
                LibraryItems.UpdateLayout();
                var container = LibraryItems.ContainerFromItem(_indexOrigin) as Control;
                if (container != null) container.Focus(FocusState.Keyboard);
            }
        }
        private void OnLibraryBack(object sender, RoutedEventArgs args)
        {
            if (IndexSurface.Visibility == Visibility.Visible) CloseIndex(true);
            else { var model = DataContext as NavidromeBrowserViewModel; if (model != null && model.CanGoBack) model.GoBack(); }
        }
        private async void OnZoomWheel(object sender, PointerRoutedEventArgs args)
        {
            if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) == 0) return;
            var delta = args.GetCurrentPoint(this).Properties.MouseWheelDelta;
            if (delta < 0 && _model != null && _model.CanShowIndex) { args.Handled = true; await ShowIndex(); }
            else if (delta > 0 && IndexSurface.Visibility == Visibility.Visible) { args.Handled = true; CloseIndex(true); }
        }
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
        private async void OnPageKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.Escape && IndexSurface.Visibility == Visibility.Visible) { args.Handled = true; CloseIndex(true); return; }
            if ((args.Key == VirtualKey.Subtract || (int)args.Key == 189) &&
                (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0 && _model != null && _model.CanShowIndex)
            { args.Handled = true; await ShowIndex(); return; }
            SearchControl.HandleShortcut(args);
        }
    }
}
