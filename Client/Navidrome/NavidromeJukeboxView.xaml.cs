namespace Mixdrome.Navidrome
{
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    public sealed partial class NavidromeJukeboxView
    {
        public NavidromeJukeboxView()
        {
            InitializeComponent();
            SizeChanged += (sender, args) => Reflow(args.NewSize.Width);
            Loaded += (sender, args) => Reflow(ActualWidth);
        }
        private void Reflow(double width)
        {
            var narrow = width < 700;
            Canvas.Margin = narrow ? new Thickness(20, 24, 20, 40) : new Thickness(88, 30, 48, 40);
            PageTitle.FontSize = width < 500 ? 32 : 56;
            OverviewColumn.Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(300);
            Grid.SetColumnSpan(Overview, narrow ? 2 : 1);
            Grid.SetColumnSpan(QueuePanel, narrow ? 2 : 1);
            Grid.SetColumn(QueuePanel, narrow ? 0 : 1); Grid.SetRow(QueuePanel, narrow ? 1 : 0);
            QueuePanel.Margin = narrow ? new Thickness(0, 28, 0, 0) : new Thickness(24, 0, 0, 0);
        }
    }
}
