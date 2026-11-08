namespace Subsonic8.Common
{
    using Caliburn.Micro;
    using Microsoft.Practices.ServiceLocation;
    using Subsonic8.Search;
    using Windows.System;
    using Windows.UI.Core;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Automation;
    using Windows.UI.Xaml.Input;

    public sealed partial class LibrarySearch
    {
        public LibrarySearch()
        {
            InitializeComponent();
            Loaded += (sender, args) =>
            {
                AutomationProperties.SetName(SearchInput, MixRadioText.Get("Search"));
                AutomationProperties.SetName(SearchButton, MixRadioText.Get("Search"));
                var model = DataContext as SearchViewModel;
                if (model != null) SearchInput.Text = model.Parameter ?? string.Empty;
            };
        }

        public void FocusQuery() { SearchInput.Focus(FocusState.Programmatic); }

        public void HandleShortcut(KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.F &&
                (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0)
            {
                FocusQuery();
                args.Handled = true;
            }
        }

        private void OnSearchKeyUp(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key != VirtualKey.Enter) return;
            args.Handled = true;
            Submit();
        }

        private void OnSearchClick(object sender, RoutedEventArgs args) { Submit(); }

        private void Submit()
        {
            var query = SearchInput.Text.Trim();
            if (query.Length == 0) return;
            ServiceLocator.Current.GetInstance<INavigationService>().NavigateToViewModel<SearchViewModel>(query);
        }
    }
}
