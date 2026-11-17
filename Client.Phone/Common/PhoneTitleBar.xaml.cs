namespace Mixdrome.Phone.Common
{
    using System;
    using Mixdrome.Phone.Services;
    using Mixdrome.Phone.Views;
    using Windows.Foundation;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Automation;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media;

    public sealed partial class PhoneTitleBar : UserControl
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register("Title", typeof(string), typeof(PhoneTitleBar), new PropertyMetadata(string.Empty, OnTextChanged));
        public static readonly DependencyProperty ElapsedProperty = DependencyProperty.Register("Elapsed", typeof(string), typeof(PhoneTitleBar), new PropertyMetadata(string.Empty, OnTextChanged));
        private PhonePlaybackObserver observer;
        private bool loaded;
        private int loadedGeneration;
        public string Title { get { return (string)GetValue(TitleProperty); } set { SetValue(TitleProperty, value); } }
        public string Elapsed { get { return (string)GetValue(ElapsedProperty); } set { SetValue(ElapsedProperty, value); } }
        public PhoneTitleBar()
        {
            InitializeComponent();
            AutomationProperties.SetName(HomeButton, PhoneText.Get("UiHome"));
            AutomationProperties.SetName(ElapsedButton, PhoneText.Get("UiNowPlaying"));
            ElapsedButton.IsEnabled = false;
            ElapsedButton.Visibility = Visibility.Collapsed;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }
        private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            var bar = (PhoneTitleBar)sender;
            if (bar.TitleText == null) return;
            bar.TitleText.Text = bar.Title ?? string.Empty;
            bar.ElapsedText.Text = bar.Elapsed ?? string.Empty;
            bar.ElapsedButton.IsEnabled = !string.IsNullOrEmpty(bar.Elapsed);
            bar.ElapsedButton.Visibility = bar.ElapsedButton.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            loaded = true;
            var generation = ++loadedGeneration;
            if (!loaded || generation != loadedGeneration || observer != null) return;
            // A player owns its own live state. Other pages expose the current
            // session through the subdued elapsed-time shortcut.
            var frame = Window.Current.Content as Frame;
            if (frame != null && frame.Content is PlaybackPage) return;
            var nextObserver = new PhonePlaybackObserver(Dispatcher);
            observer = nextObserver;
            nextObserver.Changed += OnPlaybackChanged;
            await nextObserver.StartAsync();
            if (!loaded || generation != loadedGeneration || !ReferenceEquals(observer, nextObserver)) nextObserver.Dispose();
        }
        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            loaded = false; ++loadedGeneration;
            if (observer != null) { observer.Dispose(); observer = null; }
        }
        private void OnPlaybackChanged(object sender, EventArgs args)
        {
            if (loaded && observer != null && ReferenceEquals(sender, observer)) Elapsed = observer.Track == null ? string.Empty : observer.Elapsed;
        }
        private void OnBarSizeChanged(object sender, SizeChangedEventArgs args)
        {
            var scale = args.NewSize.Width / 480;
            if (scale <= 0) return;
            Bar.Height = 60 * scale;
            HomeColumn.Width = new GridLength(60 * scale);
            HomeGlyph.Width = HomeGlyph.Height = 25 * scale;
            Divider.Width = scale; Divider.Height = 44 * scale;
            TitleText.FontSize = 21 * scale;
            TitleText.Margin = new Thickness(8 * scale, 0, 8 * scale, 0);
            ElapsedClip.Width = 200 * scale; ElapsedClip.Height = 60 * scale;
            ElapsedClip.Clip = new RectangleGeometry { Rect = new Rect(0, 0, 200 * scale, 60 * scale) };
            ElapsedText.FontSize = 91 * scale;
            ElapsedText.Margin = new Thickness(-40 * scale, -27 * scale, 0, 0);
        }
        private void OnHomeClick(object sender, RoutedEventArgs args)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null || frame.Content is MainPage) return;
            // Home is a root navigation. Let Frame select the cached home page
            // before clearing its journal, rather than pruning a live back
            // journal and then asking the legacy Frame to resolve GoBack.
            if (frame.Navigate(typeof(MainPage))) frame.BackStack.Clear();
        }
        private void OnElapsedClick(object sender, RoutedEventArgs args)
        {
            var frame = Window.Current.Content as Frame;
            if (frame != null && !(frame.Content is PlaybackPage) && !string.IsNullOrEmpty(Elapsed)) frame.Navigate(typeof(PlaybackPage));
        }
    }
}
