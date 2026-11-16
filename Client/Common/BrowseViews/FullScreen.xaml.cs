namespace Mixdrome.Common.BrowseViews
{
    public sealed partial class FullScreen
    {
        public FullScreen() { InitializeComponent(); }

        private void OnCanvasKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs args)
        {
            LibrarySearch.HandleShortcut(args);
        }
    }
}
