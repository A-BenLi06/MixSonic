namespace Mixdrome.Common
{
    using System;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media.Animation;
    using Windows.UI.Xaml.Media.Imaging;

    public sealed partial class MixRadioArtwork : UserControl
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            "Source", typeof(string), typeof(MixRadioArtwork), new PropertyMetadata(null, SourceChanged));
        public static readonly DependencyProperty DefaultSourceProperty = DependencyProperty.Register(
            "DefaultSource", typeof(string), typeof(MixRadioArtwork), new PropertyMetadata(null, SourceChanged));
        public static readonly DependencyProperty MaxOpacityProperty = DependencyProperty.Register(
            "MaxOpacity", typeof(double), typeof(MixRadioArtwork), new PropertyMetadata(1.0, SourceChanged));
        public static readonly DependencyProperty FadeDurationProperty = DependencyProperty.Register(
            "FadeDuration", typeof(int), typeof(MixRadioArtwork), new PropertyMetadata(600));
        private Storyboard _fade;
        private bool _loaded;
        private bool _ready;
        private string _displayed;

        public MixRadioArtwork()
        {
            InitializeComponent();
            ImageControl.ImageOpened += (sender, args) => { _ready = true; FadeToImage(); };
            ImageControl.ImageFailed += (sender, args) => { _ready = false; ShowDefault(); };
            Loaded += (sender, args) => { _loaded = true; UpdateSource(); };
            Unloaded += (sender, args) => { _loaded = false; if (_fade != null) _fade.Stop(); };
        }

        public string Source { get { return (string)GetValue(SourceProperty); } set { SetValue(SourceProperty, value); } }
        public string DefaultSource { get { return (string)GetValue(DefaultSourceProperty); } set { SetValue(DefaultSourceProperty, value); } }
        public double MaxOpacity { get { return (double)GetValue(MaxOpacityProperty); } set { SetValue(MaxOpacityProperty, value); } }
        public int FadeDuration { get { return (int)GetValue(FadeDurationProperty); } set { SetValue(FadeDurationProperty, value); } }
        private static void SourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        { ((MixRadioArtwork)sender).UpdateSource(); }

        private static BitmapImage Bitmap(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            Uri uri;
            if (!Uri.TryCreate(source, UriKind.Absolute, out uri)) uri = new Uri("ms-appx:///" + source.TrimStart('/'));
            return new BitmapImage(uri);
        }

        private void UpdateSource()
        {
            if (!_loaded) return;
            if (_fade != null) _fade.Stop();
            DefaultImageControl.Source = Bitmap(DefaultSource);
            if (_displayed == Source && ImageControl.Source != null)
            {
                if (_ready) { ImageControl.Opacity = MaxOpacity; DefaultImageControl.Opacity = 0; }
                else ShowDefault();
                return;
            }
            _displayed = Source;
            _ready = false;
            ImageControl.Opacity = 0;
            DefaultImageControl.Opacity = MaxOpacity;
            try { ImageControl.Source = Bitmap(Source); }
            catch (ArgumentException) { ImageControl.Source = null; }
            if (ImageControl.Source == null) ShowDefault();
        }

        private void ShowDefault()
        {
            if (_fade != null) _fade.Stop();
            ImageControl.Opacity = 0;
            DefaultImageControl.Opacity = MaxOpacity;
        }

        private void FadeToImage()
        {
            if (!_loaded || ImageControl.Source == null) return;
            if (_fade != null) _fade.Stop();
            _fade = new Storyboard();
            foreach (var target in new[] { ImageControl, DefaultImageControl })
            {
                var animation = new DoubleAnimation { To = target == ImageControl ? MaxOpacity : 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(FadeDuration)) };
                Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, "Opacity");
                _fade.Children.Add(animation);
            }
            _fade.Begin();
        }
    }
}
