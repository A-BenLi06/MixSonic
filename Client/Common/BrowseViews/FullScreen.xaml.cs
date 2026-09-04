namespace Subsonic8.Common.BrowseViews
{
    using Caliburn.Micro;
    using Microsoft.Practices.ServiceLocation;
    using Subsonic8.Search;
    using Windows.System;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;

    public sealed partial class FullScreen
    {
        public FullScreen()
        {
            InitializeComponent();
        }

        private void OnSearchInputKeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                SubmitSearch(((TextBox)sender).Text);
            }
        }

        private void OnSearchTapped(object sender, TappedRoutedEventArgs e)
        {
            SubmitSearch(SearchInput.Text);
        }

        private void SubmitSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            var navigationService = ServiceLocator.Current.GetInstance<INavigationService>();
            navigationService.NavigateToViewModel<SearchViewModel>(query);
        }
    }
}
