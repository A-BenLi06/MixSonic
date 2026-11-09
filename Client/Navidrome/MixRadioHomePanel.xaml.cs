namespace Subsonic8.Navidrome
{
    using System;
    using System.ComponentModel;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    public sealed partial class MixRadioHomePanel
    {
        public static readonly DependencyProperty UseMosaicProperty = DependencyProperty.Register(
            "UseMosaic", typeof(bool), typeof(MixRadioHomePanel), new PropertyMetadata(false));
        public bool UseMosaic { get { return (bool)GetValue(UseMosaicProperty); } set { SetValue(UseMosaicProperty, value); } }
        public event ItemClickEventHandler ItemInvoked;
        private double _width = 1366, _height = 768;
        private HomeSection _section;
        public MixRadioHomePanel()
        {
            InitializeComponent();
            Loaded += (sender, args) => { ObserveSection(); Reflow(); };
            DataContextChanged += (sender, args) => { ObserveSection(); Reflow(); };
            Unloaded += (sender, args) => { if (_section != null) _section.PropertyChanged -= SectionChanged; _section = null; };
        }
        private void ObserveSection()
        {
            if (_section != null) _section.PropertyChanged -= SectionChanged;
            _section = DataContext as HomeSection;
            if (_section != null) _section.PropertyChanged += SectionChanged;
        }
        private void SectionChanged(object sender, PropertyChangedEventArgs args) { Reflow(); }
        public void SetViewport(double width, double height) { _width = width; _height = height; Reflow(); }
        private void Reflow()
        {
            var narrow = _width < 800;
            var mosaic = UseMosaic && !narrow;
            AlbumList.Visibility = mosaic ? Visibility.Collapsed : Visibility.Visible;
            MosaicList.Visibility = mosaic ? Visibility.Visible : Visibility.Collapsed;
            PanelLayout.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
            var count = _section == null ? 1 : _section.Items.Count;
            var rows = Math.Max(1, Math.Min(3, (int)((_height - 210) / 170)));
            var columns = narrow ? Math.Max(1, Math.Min(3, (int)((_width - 40) / 170))) : (int)Math.Ceiling(count / (double)rows);
            AlbumList.Width = columns * 170;
            AlbumList.Height = narrow ? Math.Ceiling(count / (double)columns) * 170 : rows * 170;
            var panel = AlbumList.ItemsPanelRoot as WrapGrid;
            if (panel != null)
            { panel.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical; panel.MaximumRowsOrColumns = narrow ? columns : rows; }
            MosaicList.Height = Math.Max(170, Math.Min(510, _height - 210));
            SectionStatus.Visibility = _section != null && (_section.IsLoading || _section.IsEmpty || !string.IsNullOrEmpty(_section.Error)) ? Visibility.Visible : Visibility.Collapsed;
            SectionStatus.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(20, 1, 0, 0);
        }
        private void OnItemClick(object sender, ItemClickEventArgs args)
        { if (ItemInvoked != null) ItemInvoked(this, args); }
    }
}
