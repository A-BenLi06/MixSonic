namespace Mixdrome.Navidrome
{
    using Windows.UI.Xaml;
    public sealed partial class NavidromeAccountView
    {
        public NavidromeAccountView()
        {
            InitializeComponent();
            SizeChanged += (sender, args) => Reflow(args.NewSize.Width);
            Loaded += (sender, args) => Reflow(ActualWidth);
        }
        private void Reflow(double width)
        {
            Canvas.Margin = width < 700 ? new Thickness(20, 24, 20, 40) : new Thickness(88, 30, 48, 40);
            PageTitle.FontSize = width < 500 ? 32 : 56;
        }
    }
}
