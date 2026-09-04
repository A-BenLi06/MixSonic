namespace Subsonic8.Phone.Views
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Threading.Tasks;
    using Subsonic8.Phone.Services;
    using Windows.Phone.UI.Input;
    using Windows.UI.Popups;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Navigation;

    public sealed partial class RadioStationsPage : Page
    {
        private WP8SubsonicService _subsonicService;
        private ProgressRing _loadingIndicator;
        private TextBlock _emptyStateText;
        private TextBlock _stationCountText;

        public ObservableCollection<RadioStationDisplayItem> Stations { get; private set; }

        public RadioStationsPage()
        {
            Stations = new ObservableCollection<RadioStationDisplayItem>();
            InitializeComponent();
            DataContext = this;
            _loadingIndicator = FindName("LoadingIndicator") as ProgressRing;
            _emptyStateText = FindName("EmptyStateText") as TextBlock;
            _stationCountText = FindName("StationCountText") as TextBlock;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += OnHardwareBackPressed;
            _subsonicService = await PhoneSubsonicServiceFactory.CreatePlaybackServiceAsync();
            await LoadStations();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            HardwareButtons.BackPressed -= OnHardwareBackPressed;
        }

        private void OnHardwareBackPressed(object sender, BackPressedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                e.Handled = true;
                Frame.GoBack();
            }
        }

        private async Task LoadStations()
        {
            if (_loadingIndicator != null) _loadingIndicator.IsActive = true;
            if (_emptyStateText != null) _emptyStateText.Visibility = Visibility.Collapsed;

            try
            {
                var stations = await _subsonicService.GetInternetRadioStationsAsync();
                Stations.Clear();

                foreach (var station in stations)
                {
                    Stations.Add(new RadioStationDisplayItem
                    {
                        Id = station.Id,
                        Name = station.Name ?? "Untitled station",
                        StreamUrl = station.StreamUrl,
                        Subtitle = string.IsNullOrEmpty(station.HomePageUrl) ? "internet radio" : station.HomePageUrl
                    });
                }

                if (_stationCountText != null) _stationCountText.Text = string.Format("{0} stations", Stations.Count);
                if (_emptyStateText != null) _emptyStateText.Visibility = Stations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (_emptyStateText != null)
                {
                    _emptyStateText.Text = "Failed to load radio stations.";
                    _emptyStateText.Visibility = Visibility.Visible;
                }

                await new MessageDialog("Failed to load radio stations: " + ex.Message).ShowAsync();
            }
            finally
            {
                if (_loadingIndicator != null) _loadingIndicator.IsActive = false;
            }
        }

        private async void OnStationClick(object sender, ItemClickEventArgs e)
        {
            var station = e.ClickedItem as RadioStationDisplayItem;
            if (station == null || string.IsNullOrEmpty(station.StreamUrl))
            {
                await new MessageDialog("This station does not have a stream URL.").ShowAsync();
                return;
            }

            Frame.Navigate(
                typeof(PlaybackPage),
                new PlaybackNavigationParameter
                {
                    Playlist = new List<SongDisplayItem>
                    {
                        new SongDisplayItem
                        {
                            Id = station.Id ?? station.StreamUrl,
                            Title = station.Name,
                            Artist = "internet radio",
                            Duration = 0,
                            DurationString = "live",
                            StreamUrl = station.StreamUrl
                        }
                    },
                    StartIndex = 0
                });
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadStations();
        }
    }

    public class RadioStationDisplayItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string StreamUrl { get; set; }
        public string Subtitle { get; set; }
    }
}
