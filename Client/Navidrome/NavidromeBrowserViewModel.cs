namespace Subsonic8.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.EventAggregatorMessages;
    using Client.Common.Models;
    using Client.Common.Models.Navidrome;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.Navidrome;
    using Subsonic8.Playback;
    using Windows.System;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;

    public interface INavidromeBrowserViewModel : IScreen { }
    public sealed class NavidromeBrowserViewModel : NavidromeMediaPageViewModel, INavidromeBrowserViewModel
    {
        private const int PageSize = 100;
        private string _parameter;
        private int _albumOffset, _artistOffset, _songOffset;
        private bool _moreAlbums, _moreArtists, _moreSongs;
        private NavidromeApi _loadedApi;
        private string _loadedFolder;

        public NavidromeBrowserViewModel()
        {
            Items = new BindableCollection<NavidromeItem>();
            SelectedItems = new ObservableCollection<object>();
            SelectedItems.CollectionChanged += (sender, args) => Changed();
            AlbumSorts = Enum.GetValues(typeof(AlbumListType)).Cast<AlbumListType>()
                .Select(type => new NamedOption<AlbumListType> { Value = type, Name = NavidromeText.Get("Sort" + type) }).ToList();
            SelectedSort = AlbumSorts.First();
            FromYear = "1900"; ToYear = DateTime.Now.Year.ToString(CultureInfo.CurrentCulture);
            UpdateDisplayName = () => { };
        }

        public string Parameter { get { return _parameter; } set { _parameter = value; } }
        public BindableCollection<NavidromeItem> Items { get; private set; }
        public ObservableCollection<object> SelectedItems { get; private set; }
        public IList<NamedOption<AlbumListType>> AlbumSorts { get; private set; }
        public NamedOption<AlbumListType> SelectedSort { get; set; }
        public string GenreFilter { get; set; }
        public string FromYear { get; set; }
        public string ToYear { get; set; }
        public string Query { get; set; }
        public bool IsAlbums { get { return Route[0] == "albums"; } }
        public bool CanLoadMore { get { return !IsLoading && !IsWorking && (_moreAlbums || _moreArtists || _moreSongs); } }
        public bool IsEmpty { get { return !IsLoading && !HasError && Items.Count == 0; } }
        public bool CanDelete { get { return !IsWorking && SelectedItems.Any() && SelectedItems.Cast<NavidromeItem>().All(item => item.Bookmark != null || IsOwnedShare(item)); } }
        public bool CanEditShare { get { return !IsWorking && SelectedItems.Count == 1 && IsOwnedShare((NavidromeItem)SelectedItems[0]); } }
        public bool CanCopyLink { get { return !IsWorking && SelectedItems.Count == 1 && ((NavidromeItem)SelectedItems[0]).Share != null; } }
        public bool CanDetails { get { return !IsWorking && SelectedItems.Count == 1 && ((NavidromeItem)SelectedItems[0]).Media != null &&
            new[] { SubsonicModelTypeEnum.Song, SubsonicModelTypeEnum.Album, SubsonicModelTypeEnum.Artist, SubsonicModelTypeEnum.Playlist }.Contains(((NavidromeItem)SelectedItems[0]).Media.Type); } }
        private string[] Route { get { return NavidromeRoute.Parse(Parameter); } }
        protected override IList<NavidromeItem> CommandItems { get { return SelectedItems.Count > 0 ? SelectedItems.Cast<NavidromeItem>().ToList() : Items.Where(item => item.Media != null).ToList(); } }
        protected override IList<NavidromeItem> AnnotationItems { get { return SelectedItems.Cast<NavidromeItem>().ToList(); } }
        protected override Task<IList<Song>> ResolvePlaylistAsync()
        { return SelectedItems.Count > 0 ? base.ResolveCommandAsync() : Task.FromResult<IList<Song>>(new List<Song>()); }

        private bool IsOwnedShare(NavidromeItem item)
        { return item.Share != null && Session.User != null && item.Share.Username == Session.User.Username; }

        protected override void Changed()
        {
            base.Changed();
            foreach (var property in new[] { "CanLoadMore", "IsEmpty", "CanDelete", "CanEditShare", "CanCopyLink", "CanDetails", "IsAlbums" }) NotifyOfPropertyChange(property);
        }

        public override async Task Reload()
        {
            var version = ++LoadVersion;
            IsLoading = true; Error = null; SelectedItems.Clear(); Items.Clear();
            _albumOffset = _artistOffset = _songOffset = 0;
            _moreAlbums = _moreArtists = _moreSongs = false;
            try
            {
                await Session.ConnectAsync();
                var api = Session.CreateApi(); var folder = Session.FolderId; var route = Route;
                if (route[0] == "search") Query = route[1];
                DisplayName = NavidromeText.Get("Section" + route[0]);
                if (route[0] == "genre") DisplayName = route[1];
                await LoadPage(api, folder, route, version);
                if (IsCurrent(version, api)) { _loadedApi = api; _loadedFolder = folder; }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (version == LoadVersion) Error = exception.Message; }
            finally { if (version == LoadVersion) { IsLoading = false; Changed(); } }
        }

        public async Task LoadMore()
        {
            if (!CanLoadMore || _loadedApi == null) return;
            var version = LoadVersion;
            IsLoading = true; Error = null;
            try { await LoadPage(_loadedApi, _loadedFolder, Route, version); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (version == LoadVersion) Error = exception.Message; }
            finally { if (version == LoadVersion) IsLoading = false; }
        }

        private async Task LoadPage(NavidromeApi api, string folder, string[] route, int version)
        {
            var media = new List<IMediaModel>();
            var extras = new List<NavidromeItem>();
            var moreAlbums = false; var moreArtists = false; var moreSongs = false;
            var albumsRead = 0; var artistsRead = 0; var songsRead = 0;
            switch (route[0])
            {
                case "albums":
                    int from, to;
                    if (!int.TryParse(FromYear, out from) || !int.TryParse(ToYear, out to)) throw new InvalidOperationException(NavidromeText.Get("InvalidYears"));
                    if (SelectedSort.Value == AlbumListType.ByGenre && string.IsNullOrWhiteSpace(GenreFilter)) throw new InvalidOperationException(NavidromeText.Get("GenreRequired"));
                    var albums = await api.GetAlbums(new AlbumQuery { Type = SelectedSort.Value, MusicFolderId = folder, Offset = _albumOffset,
                        Size = PageSize, Genre = GenreFilter, FromYear = from, ToYear = to }).ReadAsync();
                    media.AddRange(albums); albumsRead = albums.Count;
                    moreAlbums = albums.Count == PageSize && SelectedSort.Value != AlbumListType.Random;
                    break;
                case "artists": media.AddRange(await api.GetArtists(folder).ReadAsync()); break;
                case "genres": media.AddRange(await api.GetGenres(folder).ReadAsync()); break;
                case "playlists": media.AddRange(await api.GetPlaylists().ReadAsync()); break;
                case "radio": media.AddRange(await api.GetRadio().ReadAsync()); break;
                case "activity": media.AddRange(await api.GetNowPlaying().ReadAsync()); break;
                case "random": media.AddRange(await api.GetRandomSongs(folder).ReadAsync()); break;
                case "favourites":
                    var starred = await api.GetStarred(folder).ReadAsync();
                    media.AddRange(starred.Artists); media.AddRange(starred.Albums); media.AddRange(starred.Songs); break;
                case "genre":
                    var genreSongs = await api.GetGenreSongs(route[1], folder, _songOffset, PageSize).ReadAsync();
                    media.AddRange(genreSongs); songsRead = genreSongs.Count; moreSongs = songsRead == PageSize; break;
                case "songs":
                case "search":
                    var search = await api.Search(new SearchQuery { Query = route[0] == "songs" ? string.Empty : Query,
                        MusicFolderId = folder, AlbumOffset = _albumOffset, ArtistOffset = _artistOffset, SongOffset = _songOffset,
                        AlbumCount = route[0] == "songs" || (Items.Count > 0 && !_moreAlbums) ? 0 : PageSize,
                        ArtistCount = route[0] == "songs" || (Items.Count > 0 && !_moreArtists) ? 0 : PageSize,
                        SongCount = Items.Count > 0 && !_moreSongs ? 0 : PageSize }).ReadAsync();
                    media.AddRange(search.Artists); media.AddRange(search.Albums); media.AddRange(search.Songs);
                    artistsRead = search.Artists.Count; albumsRead = search.Albums.Count; songsRead = search.Songs.Count;
                    moreArtists = artistsRead == PageSize; moreAlbums = albumsRead == PageSize; moreSongs = songsRead == PageSize; break;
                case "bookmarks":
                    foreach (var bookmark in await api.GetBookmarks().ReadAsync())
                    { var item = NavidromeItem.From(bookmark.Song, api); item.Bookmark = bookmark;
                        item.Subtitle = NavidromeItem.Join(NavidromeItem.FormatTime(bookmark.PositionMilliseconds / 1000.0), bookmark.Comment, bookmark.Song.Artist); extras.Add(item); }
                    break;
                case "shares":
                    if (Session.User == null || !Session.User.ShareRole) throw new InvalidOperationException(NavidromeText.Get("SharingUnavailable"));
                    foreach (var share in await api.GetShares().ReadAsync()) extras.Add(new NavidromeItem { Share = share,
                        Title = string.IsNullOrWhiteSpace(share.Description) ? share.Url : share.Description, Subtitle = NavidromeItem.Join(share.Username,
                        share.Expires.HasValue ? share.Expires.Value.ToLocalTime().ToString("g") : NavidromeText.Get("NeverExpires")), Kind = NavidromeText.Get("Share") });
                    break;
                default: throw new InvalidOperationException(NavidromeText.Get("UnknownSection"));
            }
            if (!IsCurrent(version, api) || folder != Session.FolderId) return;
            Items.AddRange(media.Select(item => NavidromeItem.From(item, api)).Concat(extras));
            _albumOffset += albumsRead; _artistOffset += artistsRead; _songOffset += songsRead;
            _moreAlbums = moreAlbums; _moreArtists = moreArtists; _moreSongs = moreSongs;
        }

        protected override async Task<IList<Song>> ResolveCommandAsync()
        {
            // An explicit play-all action traverses the rest of the paged context.
            if (SelectedItems.Count == 0)
                while (_moreAlbums || _moreArtists || _moreSongs)
                {
                    await LoadPage(_loadedApi, _loadedFolder, Route, LoadVersion);
                    RequireAccount(_loadedApi);
                    if (_loadedFolder != Session.FolderId || !PageActive) throw new OperationCanceledException();
                }
            return await base.ResolveCommandAsync();
        }

        public async Task OpenItem(ItemClickEventArgs args)
        {
            var item = (NavidromeItem)args.ClickedItem;
            if (_loadedApi == null || _loadedApi.AccountKey != Session.AccountKey) return;
            if (item.Share != null)
            { Uri link; if (TryWebLink(item.Share.Url, out link)) await Launcher.LaunchUriAsync(link); return; }
            if (item.Bookmark != null)
            { MediaQueue.Play(_loadedApi, new[] { item.Bookmark.Song }, 0, item.Bookmark.PositionMilliseconds); NavigationService.NavigateToViewModel<NavidromePlayerViewModel>(); return; }
            var radio = item.Media as InternetRadioStation;
            if (radio != null)
            {
                Uri uri;
                if (!TryWebLink(radio.StreamUrl, out uri)) { Error = NavidromeText.Get("InvalidStationUrl"); return; }
                EventAggregator.PublishOnUIThread(new PlayQueueMessage { Queue = new List<PlaylistItem> {
                    new PlaylistItem { Title = radio.Name, Uri = uri, Type = PlaylistItemTypeEnum.Audio, AccountKey = _loadedApi.AccountKey } }, StartIndex = 0 });
                NavigationService.NavigateToViewModel<NavidromePlayerViewModel>(); return;
            }
            if (item.Song != null)
            {
                var context = Items.Where(entry => entry.Song != null).ToList();
                MediaQueue.Play(_loadedApi, context.Select(entry => entry.Song), context.IndexOf(item));
                NavigationService.NavigateToViewModel<NavidromePlayerViewModel>(); return;
            }
            if (item.Media.Type == SubsonicModelTypeEnum.Genre)
                NavigationService.NavigateToViewModel<NavidromeBrowserViewModel>(NavidromeRoute.For("genre", item.Media.Name));
            else NavigationService.NavigateToViewModel<NavidromeDetailViewModel>(NavidromeRoute.For(item.Media.Type.ToString().ToLowerInvariant(), item.Media.Id));
        }

        public void Search(string query)
        { NavigationService.NavigateToViewModel<NavidromeBrowserViewModel>(NavidromeRoute.For("search", query)); }
        public void SearchSubmitted(SearchBoxQuerySubmittedEventArgs args) { Search(args.QueryText); }
        public void Details()
        {
            var media = SelectedItems.Cast<NavidromeItem>().Single().Media;
            if (media.Type == SubsonicModelTypeEnum.Genre) NavigationService.NavigateToViewModel<NavidromeBrowserViewModel>(NavidromeRoute.For("genre", media.Name));
            else NavigationService.NavigateToViewModel<NavidromeDetailViewModel>(NavidromeRoute.For(media.Type.ToString().ToLowerInvariant(), media.Id));
        }
        public Task Delete()
        {
            var selected = SelectedItems.Cast<NavidromeItem>().ToList();
            return RunOperation(async api =>
            {
                if (!await Dialogs.ConfirmAsync("ConfirmDelete")) return;
                foreach (var item in selected)
                {
                    RequireAccount(api);
                    if (item.Bookmark != null) await api.DeleteBookmark(item.Bookmark.Song.Id).ReadAsync();
                    else if (IsOwnedShare(item)) await api.DeleteShare(item.Share.Id).ReadAsync();
                }
                await Reload();
            });
        }

        public Task EditShare(FrameworkElement anchor)
        {
            var item = SelectedItems.Cast<NavidromeItem>().Single();
            return RunOperation(async api =>
            {
                if (!IsOwnedShare(item)) return;
                var description = new FormField { LabelKey = "Description", Value = item.Share.Description };
                var hasExpiry = new FormField { LabelKey = "ChangeExpiry", Kind = FormFieldKind.Boolean, Value = false };
                var expiry = new FormField { LabelKey = "Expires", Kind = FormFieldKind.Date, Value = item.Share.Expires ?? DateTimeOffset.Now.AddDays(7) };
                if (!await Dialogs.FormAsync(anchor, "EditShare", new[] { description, hasExpiry, expiry },
                    () => (bool)hasExpiry.Value && (DateTimeOffset)expiry.Value <= DateTimeOffset.Now ? NavidromeText.Get("FutureExpiryRequired") : null)) return;
                RequireAccount(api);
                await api.UpdateShare(item.Share.Id, (string)description.Value, (bool)hasExpiry.Value ? (DateTimeOffset?)expiry.Value : null).ReadAsync();
                await Reload();
            });
        }
        public void CopyLink()
        {
            var share = SelectedItems.Cast<NavidromeItem>().Single().Share;
            var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(share.Url);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
        }
        public static bool TryWebLink(string text, out Uri uri)
        { return Uri.TryCreate(text, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https"); }
    }
}
