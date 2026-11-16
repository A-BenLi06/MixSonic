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
    using Windows.UI.Xaml.Media;
    using Windows.UI.Xaml.Navigation;

    public sealed partial class GenresPage : Page
    {
        private ISubsonicService _subsonicService;
        private ProgressRing _loadingIndicator;
        private TextBlock _emptyStateText;

        public ObservableCollection<GenreDisplayItem> Genres { get; private set; }

        public GenresPage()
        {
            Genres = new ObservableCollection<GenreDisplayItem>();
            InitializeComponent();
            DataContext = this;
            _loadingIndicator = FindName("LoadingIndicator") as ProgressRing;
            _emptyStateText = FindName("EmptyStateText") as TextBlock;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            HardwareButtons.BackPressed += OnHardwareBackPressed;
            _subsonicService = await PhoneSubsonicServiceFactory.CreateAsync();
            await LoadGenres();
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

        private async Task LoadGenres()
        {
            if (_loadingIndicator != null) _loadingIndicator.IsActive = true;
            if (_emptyStateText != null) _emptyStateText.Visibility = Visibility.Collapsed;

            try
            {
                var result = _subsonicService.GetGenres();
                await result.Execute();

                Genres.Clear();
                if (result.Error == null && result.Result != null)
                {
                    var index = 0;
                    foreach (var genre in result.Result)
                    {
                        Genres.Add(new GenreDisplayItem
                        {
                            Name = genre.Name,
                            Subtitle = string.Format("{0} songs  {1} albums", genre.SongCount, genre.AlbumCount),
                            Glyph = GetGenreGlyph(index),
                            AccentBrush = GetAccentBrush(index++)
                        });
                    }
                }

                if (_emptyStateText != null) _emptyStateText.Visibility = Genres.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (_emptyStateText != null)
                {
                    _emptyStateText.Text = "Failed to load genres.";
                    _emptyStateText.Visibility = Visibility.Visible;
                }

                await new MessageDialog("Failed to load genres: " + ex.Message).ShowAsync();
            }
            finally
            {
                if (_loadingIndicator != null) _loadingIndicator.IsActive = false;
            }
        }

        private static Brush GetAccentBrush(int index)
        {
            var keys = new[] { "MixRadioGreenBrush", "MixRadioTealBrush", "MixRadioAccentBrush", "MixRadioYellowBrush" };
            return App.Current.Resources[keys[index % keys.Length]] as Brush;
        }

        private static string GetGenreGlyph(int index)
        {
            var glyphs = new[] { "\uE102", "\uE189", "\uE113", "\uE142" };
            return glyphs[index % glyphs.Length];
        }

        private void OnGenreClick(object sender, ItemClickEventArgs e)
        {
            var genre = e.ClickedItem as GenreDisplayItem;
            if (genre != null)
            {
                Frame.Navigate(typeof(GenreSongsPage), genre.Name);
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadGenres();
        }
    }

    public class GenreDisplayItem
    {
        public string Name { get; set; }
        public string Subtitle { get; set; }
        public string Glyph { get; set; }
        public Brush AccentBrush { get; set; }
    }
}
