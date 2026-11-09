namespace Subsonic8.Navidrome
{
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    public sealed partial class NavidromeDetailView
    {
        public NavidromeDetailView()
        {
            InitializeComponent();
            SizeChanged += (sender, args) =>
            {
                var narrow = args.NewSize.Width < 700;
                Canvas.Margin = narrow ? new Thickness(20, 24, 20, 20) : new Thickness(88, 30, 48, 40);
                PageTitle.FontSize = args.NewSize.Width < 500 ? 32 : 44;
                ArtworkColumn.Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(280);
                Artwork.Width = Artwork.Height = narrow ? 180 : 250;
                Grid.SetColumn(Information, narrow ? 0 : 1); Grid.SetRow(Information, narrow ? 1 : 0);
                Information.Margin = narrow ? new Thickness(0, 20, 0, 0) : new Thickness(0);
            };
        }
    }
}
