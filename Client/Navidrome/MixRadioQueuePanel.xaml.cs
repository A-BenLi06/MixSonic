namespace Subsonic8.Navidrome
{
    using System;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    public sealed partial class MixRadioQueuePanel
    {
        public event EventHandler CloseRequested;
        private NavidromePlayerViewModel Model { get { return DataContext as NavidromePlayerViewModel; } }
        public MixRadioQueuePanel() { InitializeComponent(); }
        public void FocusCloseButton() { CloseButton.Focus(FocusState.Programmatic); }
        private void OnQueueItemClicked(object sender, ItemClickEventArgs args) { if (Model != null) Model.PlayEntry(args); }
        private void OnShuffleClicked(object sender, RoutedEventArgs args) { if (Model != null) Model.Shuffle(); }
        private void OnRepeatClicked(object sender, RoutedEventArgs args) { if (Model != null) Model.Repeat(); }
        private void OnClearClicked(object sender, RoutedEventArgs args) { if (Model != null) Model.ClearQueue(); }
        private void OnCloseClicked(object sender, RoutedEventArgs args) { if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); }
    }
}
