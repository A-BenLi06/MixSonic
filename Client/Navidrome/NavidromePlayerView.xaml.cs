namespace Subsonic8.Navidrome
{
    using System;
    using System.ComponentModel;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Microsoft.Practices.ServiceLocation;
    using Windows.System;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Data;
    using Windows.UI.Xaml.Input;

    public sealed partial class NavidromePlayerView
    {
        private NavidromePlayerViewModel _model;
        private MixRadioPlayerSurface _surface;
        private ListView _lyrics;
        private FrameworkElement _lyricsPanel;
        private bool _loaded;
        private MenuFlyout _commandMenu;
        public NavidromePlayerView()
        {
            InitializeComponent();
            SizeChanged += (sender, args) => Reflow();
            Loaded += (sender, args) => { _loaded = true; Attach(); Reflow(); };
            Unloaded += (sender, args) =>
            {
                _loaded = false;
                CloseCommandMenu();
                if (_model != null) _model.PropertyChanged -= ModelChanged;
                _model = null; _surface = null; _lyrics = null; _lyricsPanel = null;
            };
            DataContextChanged += (sender, args) => { CloseCommandMenu(); Attach(); };
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
        private void OnTrackMenuClicked(object sender, RoutedEventArgs args)
        {
            var model = DataContext as NavidromePlayerViewModel;
            if (model == null) return;
            var anchor = (FrameworkElement)sender;
            var menu = new MenuFlyout();
            AddMenuAction(menu, model, "Navidrome_Details", nameof(model.CanDetails), model.Details);
            AddCommand(menu, model, "Navidrome_Rate", nameof(model.CanRate), () => model.Rate(anchor));
            AddCommand(menu, model, "Navidrome_Download", nameof(model.CanDownload), model.Download);
            AddCommand(menu, model, "Navidrome_ReloadLyrics", nameof(model.CanReloadLyrics), model.ReloadLyrics);
            menu.Items.Add(new MenuFlyoutSeparator());
            AddMenuAction(menu, model, "Navidrome_Stop", nameof(model.CanStop), model.Stop);
            ShowCommandMenu(menu, anchor);
        }
        private void OnQueueMenuClicked(object sender, RoutedEventArgs args)
        {
            var model = DataContext as NavidromePlayerViewModel;
            if (model == null) return;
            var anchor = (FrameworkElement)sender;
            var menu = new MenuFlyout();
            AddMenuAction(menu, model, "Navidrome_ShowQueue", null, () => OnQueueRequested(this, EventArgs.Empty));
            AddMenuAction(menu, model, "Navidrome_MoveUp", nameof(model.CanMoveUp), model.MoveUp);
            AddMenuAction(menu, model, "Navidrome_MoveDown", nameof(model.CanMoveDown), model.MoveDown);
            AddMenuAction(menu, model, "Navidrome_RemoveQueue", nameof(model.CanRemove), model.Remove);
            AddMenuAction(menu, model, "Navidrome_ClearQueue", nameof(model.CanClearQueue), model.ClearQueue);
            menu.Items.Add(new MenuFlyoutSeparator());
            AddCommand(menu, model, "Navidrome_SaveQueue", nameof(model.CanSaveQueue), model.SaveQueue);
            AddCommand(menu, model, "Navidrome_RestoreQueue", nameof(model.CanRestoreQueue), model.RestoreQueue);
            AddCommand(menu, model, "Navidrome_SaveAsPlaylist", nameof(model.CanSaveAsPlaylist), () => model.SaveAsPlaylist(anchor));
            menu.Items.Add(new MenuFlyoutSeparator());
            AddCommand(menu, model, "Navidrome_ImportQueue", nameof(model.CanImportQueue), model.ImportQueue);
            AddCommand(menu, model, "Navidrome_ExportQueue", nameof(model.CanExportQueue), model.ExportQueue);
            ShowCommandMenu(menu, anchor);
        }
        private void OnLibraryMenuClicked(object sender, RoutedEventArgs args)
        {
            var model = DataContext as NavidromePlayerViewModel;
            if (model == null) return;
            var menu = new MenuFlyout();
            AddMenuAction(menu, model, "Navidrome_Downloads", null, model.Downloads);
            AddCommand(menu, model, "Navidrome_SyncListening", nameof(model.CanSyncListening), model.SyncListening);
            menu.Items.Add(new MenuFlyoutSeparator());
            AddMenuAction(menu, model, "Navidrome_Account", null, model.Account);
            ShowCommandMenu(menu, (FrameworkElement)sender);
        }
        private void AddMenuAction(MenuFlyout menu, NavidromePlayerViewModel model, string uid, string guard, System.Action execute)
        {
            AddCommand(menu, model, uid, guard, () => { execute(); return Task.FromResult(true); });
        }
        private void AddCommand(MenuFlyout menu, NavidromePlayerViewModel model, string uid, string guard, Func<Task> execute)
        {
            var item = new MenuFlyoutItem { Text = NavidromeText.Label(uid) };
            if (guard != null) item.SetBinding(IsEnabledProperty, new Binding { Source = model, Path = new PropertyPath(guard) });
            item.Click += async (sender, args) => { if (item.IsEnabled && ReferenceEquals(model, DataContext)) await execute(); };
            menu.Items.Add(item);
        }
        private void ShowCommandMenu(MenuFlyout menu, FrameworkElement anchor)
        {
            CloseCommandMenu();
            _commandMenu = menu;
            menu.Closed += (sender, args) => { if (ReferenceEquals(_commandMenu, menu)) _commandMenu = null; };
            menu.ShowAt(anchor);
        }
        private void CloseCommandMenu()
        {
            if (_commandMenu != null) { _commandMenu.Hide(); _commandMenu = null; }
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
