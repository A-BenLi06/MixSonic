namespace Mixdrome.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services;
    using MugenInjection.Attributes;
    using Mixdrome.Playback;
    using Windows.UI.Xaml;

    public abstract class NavidromeMediaPageViewModel : NavidromePageViewModel
    {
        [Inject] public IPlaylistManagementService PlaylistQueue { get; set; }
        protected abstract IList<NavidromeItem> CommandItems { get; }
        protected virtual IList<NavidromeItem> AnnotationItems { get { return CommandItems; } }
        protected virtual Task<IList<Song>> ResolvePlaylistAsync() { return ResolveCommandAsync(); }
        protected virtual Task<IList<Song>> ResolveCommandAsync()
        { return MediaQueue.ResolveAsync(CommandItems.Where(item => item.Media != null).Select(item => (ISubsonicModel)item.Media).ToList()); }
        public bool CanPlay { get { return !IsWorking && !IsLoading && CommandItems.Any() && CommandItems.All(item => item.Media != null && IsPlayable(item.Media.Type)); } }
        public bool CanPlayNext { get { return CanPlay; } }
        public bool CanAppend { get { return CanPlay; } }
        public bool CanFavourite { get { return !IsWorking && !IsLoading && AnnotationItems.Any() && AnnotationItems.All(CanAnnotate); } }
        public bool CanUnfavourite { get { return CanFavourite; } }
        public bool CanRate { get { return CanFavourite; } }
        public bool CanCreatePlaylist { get { return !IsWorking; } }
        public bool CanAddToPlaylist { get { return CanPlay && AnnotationItems.Any(); } }
        public bool CanCreateShare { get { return CanPlay && AnnotationItems.Any() && Session.User != null && Session.User.ShareRole; } }
        public bool CanDownload { get { return CanPlay && Session.User != null && Session.User.DownloadRole; } }
        private static bool IsPlayable(SubsonicModelTypeEnum type)
        { return type == SubsonicModelTypeEnum.Song || type == SubsonicModelTypeEnum.Album || type == SubsonicModelTypeEnum.Artist || type == SubsonicModelTypeEnum.Playlist || type == SubsonicModelTypeEnum.Genre; }

        private static bool CanAnnotate(NavidromeItem item)
        { return item.Media is Song || item.Media is Album || item.Media is ExpandedArtist || item.Media is Playlist; }

        protected override void Changed()
        {
            base.Changed();
            foreach (var property in new[] { "CanPlay", "CanPlayNext", "CanAppend", "CanFavourite", "CanUnfavourite", "CanRate", "CanCreatePlaylist", "CanAddToPlaylist", "CanCreateShare", "CanDownload" }) NotifyOfPropertyChange(property);
        }

        public Task Play() { return QueueCommand(true, false); }
        public Task PlayNext() { return QueueCommand(false, true); }
        public Task Append() { return QueueCommand(false, false); }
        public Task Download()
        {
            return RunOperation(async api =>
            {
                var songs = await ResolveCommandAsync(); RequireAccount(api);
                await Offline.EnqueueAsync(api, songs); RequireAccount(api);
                NavigationService.NavigateToViewModel<NavidromeDownloadsViewModel>();
            });
        }
        private Task QueueCommand(bool play, bool next)
        {
            return RunOperation(async api =>
            {
                var songs = await ResolveCommandAsync();
                RequireAccount(api);
                if (play)
                {
                    MediaQueue.Play(api, songs);
                    if (songs.Count > 0) NavigationService.NavigateToViewModel<NavidromePlayerViewModel>();
                }
                else MediaQueue.Append(api, songs, next);
            });
        }

        public Task Favourite() { return SetStarred(true); }
        public Task Unfavourite() { return SetStarred(false); }
        private Task SetStarred(bool value)
        {
            var selection = AnnotationItems.Where(CanAnnotate).ToList();
            return RunOperation(async api =>
            {
                foreach (var item in selection)
                {
                    RequireAccount(api);
                    await api.SetStarred(item.Media, value).ReadAsync();
                    RequireAccount(api); item.UpdateStarred(value);
                    if (item.Media.Type == SubsonicModelTypeEnum.Song)
                        foreach (var queued in PlaylistQueue.Items.Where(queued => queued.AccountKey == api.AccountKey && queued.SongId == item.Media.Id)) queued.IsStarred = value;
                }
            });
        }

        public Task Rate(FrameworkElement anchor)
        {
            var selection = AnnotationItems.Where(CanAnnotate).ToList();
            return RunOperation(async api =>
            {
                var field = new FormField { LabelKey = "Rating", Kind = FormFieldKind.Choice,
                    Choices = Enumerable.Range(0, 6).Cast<object>().ToList(), Value = selection.Count == 1 ? ((SubsonicModelBase)selection[0].Media).UserRating : 0 };
                if (!await Dialogs.FormAsync(anchor, "Rate", new[] { field })) return;
                foreach (var item in selection)
                {
                    RequireAccount(api); await api.SetRating(item.Media.Id, (int)field.Value).ReadAsync();
                    RequireAccount(api); ((SubsonicModelBase)item.Media).UserRating = (int)field.Value;
                    if (item.Media.Type == SubsonicModelTypeEnum.Song)
                        foreach (var queued in PlaylistQueue.Items.Where(queued => queued.AccountKey == api.AccountKey && queued.SongId == item.Media.Id)) queued.UserRating = (int)field.Value;
                }
                await Reload();
            });
        }

        public Task CreatePlaylist(FrameworkElement anchor)
        {
            return RunOperation(async api =>
            {
                var songs = await ResolvePlaylistAsync();
                var name = new FormField { LabelKey = "Name" };
                if (!await Dialogs.FormAsync(anchor, "CreatePlaylist", new[] { name },
                    () => string.IsNullOrWhiteSpace((string)name.Value) ? NavidromeText.Get("NameRequired") : null)) return;
                RequireAccount(api);
                var playlist = await api.SavePlaylist((string)name.Value, songs.Select(song => song.Id)).ReadAsync();
                RequireAccount(api); NavigationService.NavigateToViewModel<NavidromeDetailViewModel>(NavidromeRoute.For("playlist", playlist.Id));
            });
        }

        public Task AddToPlaylist(FrameworkElement anchor)
        {
            return RunOperation(async api =>
            {
                var songs = await ResolveCommandAsync();
                var playlists = (await api.GetPlaylists().ReadAsync()).Where(playlist => !playlist.ReadOnly &&
                    Session.User != null && playlist.Owner == Session.User.Username).Cast<object>().ToList();
                RequireAccount(api);
                if (playlists.Count == 0) { Error = NavidromeText.Get("NoWritablePlaylists"); return; }
                var target = new FormField { LabelKey = "Playlist", Kind = FormFieldKind.Choice, Choices = playlists,
                    Value = playlists[0], DisplayMemberPath = "Name" };
                if (!await Dialogs.FormAsync(anchor, "AddToPlaylist", new[] { target })) return;
                RequireAccount(api);
                await api.UpdatePlaylist(((Playlist)target.Value).Id, null, null, null, songs.Select(song => song.Id), null).ReadAsync();
                await Reload();
            });
        }

        public Task CreateShare(FrameworkElement anchor)
        {
            return RunOperation(async api =>
            {
                if (Session.User == null || !Session.User.ShareRole) throw new InvalidOperationException(NavidromeText.Get("SharingUnavailable"));
                var songs = await ResolveCommandAsync();
                var description = new FormField { LabelKey = "Description" };
                var hasExpiry = new FormField { LabelKey = "SetExpiry", Kind = FormFieldKind.Boolean, Value = false };
                var expiry = new FormField { LabelKey = "Expires", Kind = FormFieldKind.Date, Value = DateTimeOffset.Now.AddDays(7) };
                if (!await Dialogs.FormAsync(anchor, "CreateShare", new[] { description, hasExpiry, expiry },
                    () => (bool)hasExpiry.Value && (DateTimeOffset)expiry.Value <= DateTimeOffset.Now ? NavidromeText.Get("FutureExpiryRequired") : null)) return;
                RequireAccount(api);
                await api.CreateShare(songs.Select(song => song.Id), description.Value as string,
                    (bool)hasExpiry.Value ? (DateTimeOffset?)expiry.Value : null).ReadAsync();
                RequireAccount(api); NavigationService.NavigateToViewModel<NavidromeBrowserViewModel>("shares");
            });
        }
    }
}
