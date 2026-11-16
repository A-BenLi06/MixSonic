namespace Mixdrome.Phone.Views
{
    using System;
    using System.Collections.ObjectModel;
    using System.Threading.Tasks;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services;
    using Mixdrome.Phone.Services;
    using Windows.Phone.UI.Input;
    using Windows.UI.Popups;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Navigation;

    public sealed partial class ArtistsPage : Page
    {
        private bool _pageActive;
        private ISubsonicService _subsonicService;
        private ProgressRing _loadingIndicator;
        private TextBlock _emptyStateText;

        public ObservableCollection<ArtistDisplayItem> Artists { get; private set; }

        public ArtistsPage()
        {
            Artists = new ObservableCollection<ArtistDisplayItem>();
            InitializeComponent();
            DataContext = this;

            _loadingIndicator = FindName("LoadingIndicator") as ProgressRing;
            _emptyStateText = FindName("EmptyStateText") as TextBlock;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _pageActive = true;
            HardwareButtons.BackPressed += OnHardwareBackPressed;
            try
            {
                _subsonicService = await PhoneSubsonicServiceFactory.CreateAsync();
                if (!_subsonicService.HasValidSubsonicUrl)
                    throw new InvalidOperationException(Mixdrome.Phone.Common.PhoneText.Get("ConfigureServer"));
                if (!_pageActive) return;
                await LoadArtists();
            }
            catch (Exception exception)
            {
                if (_pageActive && _emptyStateText != null)
                {
                    _emptyStateText.Text = exception.Message;
                    _emptyStateText.Visibility = Visibility.Visible;
                }
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _pageActive = false;
            HardwareButtons.BackPressed -= OnHardwareBackPressed;
        }

        private void OnHardwareBackPressed(object sender, BackPressedEventArgs e)
        {
            if (e.Handled) return;
            if (Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        private async Task LoadArtists()
        {
            if (_loadingIndicator != null) _loadingIndicator.IsActive = true;
            if (_emptyStateText != null) _emptyStateText.Visibility = Visibility.Collapsed;

            try
            {
                var result = _subsonicService.GetArtists();
                await result.Execute();
                if (!_pageActive) return;
                if (result.Error != null) throw result.Error;

                Artists.Clear();
                if (result.Result != null)
                {
                    foreach (var artist in result.Result)
                    {
                        Artists.Add(new ArtistDisplayItem
                        {
                            Id = artist.Id,
                            Name = artist.Name ?? Mixdrome.Phone.Common.PhoneText.Get("UnknownArtist"),
                            Subtitle = string.Format(Mixdrome.Phone.Common.PhoneText.Get("AlbumCount"), artist.AlbumCount),
                            Initial = GetInitial(artist.Name)
                        });
                    }
                }

                if (_emptyStateText != null)
                {
                    _emptyStateText.Visibility = Artists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                if (!_pageActive) return;
                if (_emptyStateText != null)
                {
                    _emptyStateText.Text = Mixdrome.Phone.Common.PhoneText.Get("UiFailedToLoadArtists");
                    _emptyStateText.Visibility = Visibility.Visible;
                }

                await new MessageDialog(Mixdrome.Phone.Common.PhoneText.Get("UiFailedToLoadArtists") + ex.Message).ShowAsync();
            }
            finally
            {
                if (_loadingIndicator != null) _loadingIndicator.IsActive = false;
            }
        }

        private static string GetInitial(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? "?" : name.Trim().Substring(0, 1).ToUpperInvariant();
        }

        private void OnArtistClick(object sender, ItemClickEventArgs e)
        {
            var artist = e.ClickedItem as ArtistDisplayItem;
            if (artist != null)
            {
                Frame.Navigate(typeof(ArtistAlbumsPage), artist);
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadArtists();
        }
    }

    public class ArtistDisplayItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Subtitle { get; set; }
        public string Initial { get; set; }
    }
}
