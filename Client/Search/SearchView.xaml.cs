namespace Subsonic8.Search
{
    using Caliburn.Micro;
    using Microsoft.Practices.ServiceLocation;
    using Windows.System;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;

    public sealed partial class SearchView
    {
        #region Constructors and Destructors

        public SearchView()
        {
            InitializeComponent();
            Loaded += (sender, e) =>
                {
                    var viewModel = DataContext as SearchViewModel;
                    if (viewModel != null && !string.IsNullOrWhiteSpace(viewModel.Parameter))
                    {
                        QueryInput.Text = viewModel.Parameter;
                    }
                };
        }

        #endregion

        #region Methods

        private void OnQueryInputKeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                SubmitSearch(((TextBox)sender).Text);
            }
        }

        private void OnSearchTapped(object sender, TappedRoutedEventArgs e)
        {
            SubmitSearch(QueryInput.Text);
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

        #endregion
    }
}
