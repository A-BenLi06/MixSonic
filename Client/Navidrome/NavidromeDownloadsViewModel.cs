namespace Subsonic8.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.EventAggregatorMessages;
    using Client.Common.Services;
    using Client.Common.Models.Navidrome;
    using MugenInjection.Attributes;
    using Subsonic8.Playback;
    using Windows.Storage;
    using Windows.Storage.Pickers;
    using Windows.Storage.Provider;
    using Windows.UI.Xaml.Controls;

    public interface INavidromeDownloadsViewModel : IScreen { }
    public sealed class NavidromeDownloadsViewModel : NavidromePageViewModel, INavidromeDownloadsViewModel
    {
        public NavidromeDownloadsViewModel()
        {
            SelectedItems = new ObservableCollection<object>();
            SelectedItems.CollectionChanged += (sender, args) => Changed();
            UpdateDisplayName = () => { };
        }
        [Inject] public IPlaylistManagementService PlaylistQueue { get; set; }
        public ObservableCollection<object> SelectedItems { get; private set; }
        private IList<OfflineDownloadItem> Selection { get { return SelectedItems.Cast<OfflineDownloadItem>().ToList(); } }
        private IList<OfflineDownloadItem> Playable { get { return SelectedItems.Count > 0 ? Selection : Offline.Entries.Where(item => item.CanPlay).ToList(); } }
        public bool IsEmpty { get { return !IsLoading && !HasError && Offline.Entries.Count == 0; } }
        public bool CanPlay { get { return !IsWorking && Playable.Count > 0 && Playable.All(item => item.CanPlay); } }
        public bool CanAppend { get { return CanPlay; } }
        public bool CanExport { get { return !IsWorking && Selection.Count == 1 && Selection[0].OriginalAvailable; } }
        public bool CanCancel { get { return Selection.Any(item => item.CanCancel); } }
        public bool CanCancelAll { get { return Offline.Entries.Any(item => item.CanCancel); } }
        public bool CanRetry { get { return !IsWorking && Selection.Count > 0 && Selection.All(item => item.CanRetry); } }
        public bool CanDelete { get { return !IsWorking && Selection.Count > 0; } }

        protected override void OnActivate()
        {
            Offline.PropertyChanged += OfflineChanged;
            base.OnActivate();
        }
        protected override void OnDeactivate(bool close) { Offline.PropertyChanged -= OfflineChanged; base.OnDeactivate(close); }
        private void OfflineChanged(object sender, PropertyChangedEventArgs args) { Changed(); }
        protected override void Changed()
        {
            base.Changed();
            foreach (var name in new[] { "IsEmpty", "CanPlay", "CanAppend", "CanExport", "CanCancel", "CanCancelAll", "CanRetry", "CanDelete" }) NotifyOfPropertyChange(name);
        }
        public override async Task Reload()
        {
            var version = ++LoadVersion; IsLoading = true; Error = null; DisplayName = NavidromeText.Get("Downloads"); SelectedItems.Clear();
            try { await Offline.RefreshAsync(); }
            catch (Exception exception) { if (version == LoadVersion) Error = exception.Message; }
            finally { if (version == LoadVersion) IsLoading = false; }
        }
        public Task Play() { return StartLocal(Playable, 0); }
        private Task StartLocal(IList<OfflineDownloadItem> items, int index)
        {
            return RunLocalOperation(async () =>
            {
                var queue = await Offline.CreateQueueAsync(items); MediaQueue.PlayLocal(queue, index);
                if (queue.Count > 0) NavigationService.NavigateToViewModel<PlaybackViewModel>();
            });
        }
        public Task OpenItem(ItemClickEventArgs args)
        {
            var item = (OfflineDownloadItem)args.ClickedItem;
            var context = Offline.Entries.Where(entry => entry.CanPlay).ToList();
            return item.CanPlay ? StartLocal(context, context.IndexOf(item)) : Task.FromResult(false);
        }
        public Task Append() { var items = Playable; return RunLocalOperation(async () => MediaQueue.AppendLocal(await Offline.CreateQueueAsync(items))); }
        public void Cancel() { foreach (var item in Selection) Offline.Cancel(item); Changed(); }
        public void CancelAll() { Offline.CancelAll(); Changed(); }
        public Task Retry() { var items = Selection; return RunLocalOperation(async () => { foreach (var item in items) await Offline.RetryAsync(item); SelectedItems.Clear(); }); }
        public Task Delete()
        {
            var items = Selection;
            return RunLocalOperation(async () =>
            {
                if (!await Dialogs.ConfirmAsync("ConfirmDeleteDownloads")) return;
                var queued = PlaylistQueue.Items.Where(track => track.AccountKey == Session.AccountKey && track.Uri != null && track.Uri.Scheme == "ms-appdata" &&
                    items.Any(item => item.Song.Id == track.SongId)).ToList();
                PlaylistQueue.Handle(new RemoveItemsMessage { Queue = queued });
                await Offline.DeleteAsync(items); SelectedItems.Clear();
            });
        }
        public Task Export()
        {
            var item = Selection.Single();
            return RunLocalOperation(async () =>
            {
                var source = await Offline.GetOriginalAsync(item);
                var picker = new FileSavePicker { SuggestedFileName = OfflineCatalog.ExportName(item.Song), SuggestedStartLocation = PickerLocationId.MusicLibrary };
                picker.FileTypeChoices.Add(NavidromeText.Get("OriginalFile"), new List<string> { "." + OfflineCatalog.Suffix(item.Song.Suffix) });
                var target = await picker.PickSaveFileAsync();
                if (target == null) return;
                if (item.AccountKey != Session.AccountKey) throw new OperationCanceledException();
                CachedFileManager.DeferUpdates(target); await source.CopyAndReplaceAsync(target);
                if (await CachedFileManager.CompleteUpdatesAsync(target) != FileUpdateStatus.Complete)
                    throw new InvalidOperationException(NavidromeText.Get("ExportFailed"));
            });
        }
    }
}
