namespace Mixdrome.Search
{
    public sealed partial class SearchView
    {
        public SearchView() { InitializeComponent(); }

        private void OnCanvasKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs args)
        {
            LibrarySearch.HandleShortcut(args);
        }
    }
}
