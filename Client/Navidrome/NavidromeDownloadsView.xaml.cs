namespace Subsonic8.Navidrome
{
    using Windows.UI.Xaml;
    public sealed partial class NavidromeDownloadsView
    {
        public NavidromeDownloadsView()
        {
            InitializeComponent();
            SizeChanged += (sender, args) =>
            {
                Canvas.Margin = args.NewSize.Width < 700 ? new Thickness(20, 24, 20, 20) : new Thickness(88, 30, 48, 40);
                PageTitle.FontSize = args.NewSize.Width < 500 ? 32 : 44;
            };
        }
    }
}
