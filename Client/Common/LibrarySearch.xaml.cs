namespace Subsonic8.Common
{
    using Caliburn.Micro;
    using Microsoft.Practices.ServiceLocation;
    using Subsonic8.Navidrome;
    using Subsonic8.Search;
    using Windows.System;
    using Windows.UI.Core;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;

    public sealed partial class LibrarySearch
    {
        public LibrarySearch()
        {
            InitializeComponent();
            Loaded += (sender, args) =>
            {
                var library = DataContext as NavidromeBrowserViewModel;
                var legacy = DataContext as SearchViewModel;
                SearchInput.QueryText = library != null ? library.Query ?? string.Empty : legacy != null ? legacy.Parameter ?? string.Empty : string.Empty;
            };
        }
        public void FocusQuery() { SearchInput.Focus(FocusState.Programmatic); }
        public void HandleShortcut(KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.F &&
                (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0)
            { FocusQuery(); args.Handled = true; }
        }
        private void OnQuerySubmitted(SearchBox sender, SearchBoxQuerySubmittedEventArgs args)
        {
            var query = (args.QueryText ?? string.Empty).Trim();
            if (query.Length == 0) return;
            ServiceLocator.Current.GetInstance<INavigationService>().NavigateToViewModel<NavidromeBrowserViewModel>(NavidromeRoute.For("search", query));
        }
    }
}
