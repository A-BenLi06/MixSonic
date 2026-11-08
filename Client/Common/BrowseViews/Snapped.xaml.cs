namespace Subsonic8.Common.BrowseViews
{
    public sealed partial class Snapped
    {
        public Snapped()
        {
            InitializeComponent();
        }

        private void OnCanvasKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs args)
        {
            LibrarySearch.HandleShortcut(args);
        }
    }
}
