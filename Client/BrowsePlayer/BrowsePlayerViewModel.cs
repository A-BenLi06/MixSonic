namespace Mixdrome.BrowsePlayer
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.EventAggregatorMessages;
    using Client.Common.Models;
    using Client.Common.Services;
    using Mixdrome.Framework.Extensions;
    using Mixdrome.Framework.ViewModel;
    using Mixdrome.Listening;
    using Mixdrome.Playback;
    using Mixdrome.Navidrome;
    using MugenInjection.Attributes;
    using Windows.UI.Xaml.Controls;

    public class BrowsePlayerViewModel : PlaybackControlsViewModelBase, IBrowsePlayerViewModel
    {
        private readonly IPlaylistManagementService _playlist;
        private readonly IListeningHistoryService _history;
        private bool _favouriteBusy;
        private PlaylistItem _observedItem;

        public BrowsePlayerViewModel(IPlaylistManagementService playlist, IListeningHistoryService history)
        {
            _playlist = playlist;
            _history = history;
            playlist.PropertyChanged += OnPlayerChanged;
            history.PropertyChanged += (sender, args) =>
            {
                NotifyOfPropertyChange(() => RecentTracks);
                NotifyOfPropertyChange(() => HasRecentTracks);
                NotifyOfPropertyChange(() => HistoryError);
                NotifyOfPropertyChange(() => CanToggleFavourite);
            };
        }

        [Inject] public IProgressIndicatorViewModel Progress { get; set; }

        public PlaylistItem CurrentItem { get { return _playlist.CurrentItem; } }
        public bool HasQueue { get { return _playlist.HasElements; } }
        public bool HasCurrentItem { get { return CurrentItem != null; } }
        public bool IsPlaying { get { return _playlist.IsPlaying; } }
        public string Title { get { return HasCurrentItem ? CurrentItem.Title : MixRadioText.Get("PlayerEmptyTitle"); } }
        public string Artist { get { return HasCurrentItem ? CurrentItem.Artist : MixRadioText.Get("PlayerEmptySubtitle"); } }
        public string PlayPauseLabel { get { return MixRadioText.Get(IsPlaying ? "Pause" : "Play"); } }
        public string PlayPauseGlyph { get { return IsPlaying ? "\uE103" : "\uE102"; } }
        public string RepeatLabel { get { return MixRadioText.Get("Repeat" + _playlist.RepeatMode); } }
        public string ShuffleLabel { get { return MixRadioText.Get(_playlist.ShuffleOn ? "ShuffleOn" : "ShuffleOff"); } }
        public string FavouriteLabel { get { return MixRadioText.Get(HasCurrentItem && CurrentItem.IsStarred ? "FavouriteRemove" : "FavouriteAdd"); } }
        public string PreviousLabel { get { return MixRadioText.Get("Previous"); } }
        public string NextLabel { get { return MixRadioText.Get("Next"); } }
        public string QueueLabel { get { return MixRadioText.Get("Queue"); } }
        public IEnumerable<RecentTrackViewModel> RecentTracks { get { return _history.Entries.Select(track => new RecentTrackViewModel(track)); } }
        public bool HasRecentTracks { get { return _history.Entries.Any(); } }
        public string RecentEmptyLabel { get { return MixRadioText.Get("RecentEmpty"); } }
        public string HistoryError { get { return _history.Error; } }
        public bool CanToggleFavourite
        {
            get { return !_favouriteBusy && HasCurrentItem && !string.IsNullOrWhiteSpace(CurrentItem.SongId) &&
                         CurrentItem.Type == PlaylistItemTypeEnum.Audio &&
                         CurrentItem.AccountKey == AccountIdentity.For(SubsonicService.Configuration); }
        }

        public Task LoadRecentAsync() { return _history.RefreshAsync(); }
        public void ToggleRepeat() { EventAggregator.PublishOnUIThread(new ToggleRepeatMessage()); }
        public void ToggleShuffle() { EventAggregator.PublishOnUIThread(new ToggleShuffleMessage()); }
        public void OpenQueue() { NavigationService.NavigateToViewModel<NavidromePlayerViewModel>(); }

        public async Task ToggleFavourite()
        {
            if (!CanToggleFavourite) return;
            var item = CurrentItem;
            var starred = !item.IsStarred;
            _favouriteBusy = true;
            NotifyOfPropertyChange(() => CanToggleFavourite);
            try
            {
                await SubsonicService.SetStarred(item.SongId, starred).WithErrorHandler(ErrorDialogViewModel)
                    .OnSuccess(result => { if (result) item.IsStarred = starred; }).Execute();
            }
            finally
            {
                _favouriteBusy = false;
                NotifyOfPropertyChange(() => CanToggleFavourite);
                NotifyOfPropertyChange(() => FavouriteLabel);
            }
        }

        public async Task ReplayRecent(ItemClickEventArgs args)
        {
            var recent = args.ClickedItem as RecentTrackViewModel;
            if (recent == null) return;
            var track = recent.Track;
            var account = AccountIdentity.For(SubsonicService.Configuration);
            await SubsonicService.GetSong(track.SongId).WithErrorHandler(ErrorDialogViewModel)
                .OnSuccess(song =>
                {
                    if (account != AccountIdentity.For(SubsonicService.Configuration)) return;
                    EventAggregator.PublishOnUIThread(new PlayQueueMessage
                    {
                        Queue = new List<PlaylistItem> { song.AsPlaylistItem(SubsonicService) }, StartIndex = 0
                    });
                }).Execute();
        }

        private void OnPlayerChanged(object sender, PropertyChangedEventArgs args)
        {
            if (!ReferenceEquals(_observedItem, CurrentItem))
            {
                if (_observedItem != null) _observedItem.PropertyChanged -= OnItemChanged;
                _observedItem = CurrentItem;
                if (_observedItem != null) _observedItem.PropertyChanged += OnItemChanged;
            }
            NotifyOfPropertyChange(() => CurrentItem);
            NotifyOfPropertyChange(() => HasCurrentItem);
            NotifyOfPropertyChange(() => HasQueue);
            NotifyOfPropertyChange(() => Title);
            NotifyOfPropertyChange(() => Artist);
            NotifyOfPropertyChange(() => IsPlaying);
            NotifyOfPropertyChange(() => PlayPauseGlyph);
            NotifyOfPropertyChange(() => PlayPauseLabel);
            NotifyOfPropertyChange(() => RepeatLabel);
            NotifyOfPropertyChange(() => ShuffleLabel);
            NotifyOfPropertyChange(() => CanToggleFavourite);
            NotifyOfPropertyChange(() => FavouriteLabel);
        }

        private void OnItemChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == "IsStarred") NotifyOfPropertyChange(() => FavouriteLabel);
        }
    }
}
