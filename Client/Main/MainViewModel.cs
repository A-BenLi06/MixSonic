namespace Mixdrome.Main
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Client.Common.Models.Navidrome;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.Navidrome;
    using MugenInjection.Attributes;
    using Mixdrome.Framework.Services;
    using Mixdrome.Framework.ViewModel;
    using Mixdrome.Navidrome;
    using Mixdrome.Settings;

    public sealed class MainViewModel : NavidromePageViewModel, IMainViewModel, IHaveState
    {
        private NavidromeApi _api;
        public MainViewModel()
        {
            Destinations = LibraryDestination.All();
            Recent = Section("albums", "RecentAlbums", "NoRecentAlbums", "\uE1AB");
            Playlists = Section("playlists", "Sectionplaylists", "NoHomePlaylists", "\uE142");
            Artists = Section("artists", "Sectionartists", "NoHomeArtists", "\uE13D");
            Downloads = Section("downloads", "Downloads", "NoHomeDownloads", "\uE118");
            Favourites = Section("favourites", "Sectionfavourites", "NoHomeFavourites", "\uE113");
            MyMusic = Section("albums", "Sectionalbums", "NoHomeAlbums", "\uE111");
            UpdateDisplayName = () => DisplayName = NavidromeText.Get("Home");
        }
        [Inject] public INavidromePlayerViewModel HomePlayer { get; set; }
        [Inject] public IDialogService DialogService { get; set; }
        public NavidromePlayerViewModel Player { get { return (NavidromePlayerViewModel)HomePlayer; } }
        public IList<LibraryDestination> Destinations { get; private set; }
        public HomeSection Recent { get; private set; }
        public HomeSection Playlists { get; private set; }
        public HomeSection Artists { get; private set; }
        public HomeSection Downloads { get; private set; }
        public HomeSection Favourites { get; private set; }
        public HomeSection MyMusic { get; private set; }
        private IEnumerable<HomeSection> Sections { get { return new[] { Recent, Playlists, Artists, Downloads, Favourites, MyMusic }; } }
        private static HomeSection Section(string route, string title, string empty, string glyph)
        { return new HomeSection(route, NavidromeText.Get(title), NavidromeText.Get(empty), glyph); }

        protected override void OnActivate()
        {
            ((IActivate)HomePlayer).Activate();
            base.OnActivate();
        }
        protected override void OnDeactivate(bool close)
        {
            ((IDeactivate)HomePlayer).Deactivate(false);
            _api = null;
            base.OnDeactivate(close);
        }
        public Task Populate() { return Reload(); }
        public void SaveState(Dictionary<string, object> state, List<Type> knownTypes)
        { ((IHaveState)HomePlayer).SaveState(state, knownTypes); }
        public void LoadState(string parameter, Dictionary<string, object> state)
        { ((IHaveState)HomePlayer).LoadState(parameter, state); }
        public override async Task Reload()
        {
            var version = ++LoadVersion;
            _api = null; Error = null; IsLoading = true;
            foreach (var section in Sections)
            { section.Error = null; section.IsLoading = true; section.Replace(Enumerable.Empty<NavidromeItem>()); }
            try
            {
                await LoadDownloads(version);
                await Session.ConnectAsync();
                var api = Session.CreateApi(); var folder = Session.FolderId;
                if (!IsCurrent(version, api)) return;
                _api = api;
                await Task.WhenAll(
                    LoadSection(Recent, async () => (await api.GetAlbums(new AlbumQuery { Type = AlbumListType.Recent, Size = 5, MusicFolderId = folder }).ReadAsync()).Cast<IMediaModel>(), api, folder, version),
                    LoadSection(MyMusic, async () => (await api.GetAlbums(new AlbumQuery { Type = AlbumListType.Newest, Size = 5, MusicFolderId = folder }).ReadAsync()).Cast<IMediaModel>(), api, folder, version),
                    LoadSection(Playlists, async () => (await api.GetPlaylists().ReadAsync()).Take(5).Cast<IMediaModel>(), api, folder, version, true),
                    LoadSection(Artists, async () => (await api.GetArtists(folder).ReadAsync()).Take(5).Cast<IMediaModel>(), api, folder, version),
                    LoadSection(Favourites, async () => {
                        var starred = await api.GetStarred(folder).ReadAsync();
                        return starred.Artists.Cast<IMediaModel>().Concat(starred.Albums).Concat(starred.Songs).Take(5);
                    }, api, folder, version));
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (version == LoadVersion) Error = exception.Message; }
            finally
            {
                if (version == LoadVersion)
                { IsLoading = false; foreach (var section in Sections) section.IsLoading = false; }
            }
        }
        private async Task LoadSection(HomeSection section, Func<Task<IEnumerable<IMediaModel>>> load,
            NavidromeApi api, string folder, int version, bool hero = false)
        {
            try
            {
                var media = await load();
                if (IsCurrent(version, api) && folder == Session.FolderId)
                    section.Replace(media.Select(item => NavidromeItem.From(item, api)), hero);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (IsCurrent(version, api)) section.Error = exception.Message; }
            finally { if (version == LoadVersion) section.IsLoading = false; }
        }
        private async Task LoadDownloads(int version)
        {
            try
            {
                await Offline.RefreshAsync();
                if (PageActive && version == LoadVersion)
                    Downloads.Replace(Offline.Entries.Where(item => item.CanPlay && item.AccountKey == Session.AccountKey).Take(5)
                        .Select(item => new NavidromeItem { Media = item.Song, Download = item, Title = item.Title,
                            Subtitle = item.Subtitle, ImageUrl = item.ImageUrl }));
            }
            catch (Exception exception) { if (version == LoadVersion) Downloads.Error = exception.Message; }
            finally { if (version == LoadVersion) Downloads.IsLoading = false; }
        }

        public void OpenDestination(LibraryDestination destination)
        {
            if (destination == null) throw new ArgumentNullException("destination");
            destination.Navigate(NavigationService);
        }
        public void PlayMe() { OpenDestination(Destinations.Single(item => item.Route == "random")); }
        public void Settings() { DialogService.ShowSettings<SettingsViewModel>(); }
        public void OpenPlayer() { NavigationService.NavigateToViewModel<NavidromePlayerViewModel>(); }
        public async Task<bool> OpenTile(object tile)
        {
            var destination = tile as LibraryDestination;
            if (destination != null) { OpenDestination(destination); return false; }
            var item = tile as NavidromeItem;
            if (item == null || IsWorking || !PageActive) return false;
            var version = LoadVersion;
            var account = Session.AccountKey;
            var folder = Session.FolderId;
            if (item.Download != null)
            {
                var started = false;
                await RunLocalOperation(async () => {
                    var context = Downloads.Items.OfType<NavidromeItem>().Where(entry => entry.Download != null && entry.Download.CanPlay).ToList();
                    var index = context.IndexOf(item);
                    if (index < 0) return;
                    var queue = await Offline.CreateQueueAsync(context.Select(entry => entry.Download));
                    if (!PageActive || version != LoadVersion || account != Session.AccountKey || queue.Count != context.Count) return;
                    MediaQueue.PlayLocal(queue, index); started = queue.Count > 0;
                });
                return started;
            }
            if (item.Media == null || _api == null || _api.AccountKey != Session.AccountKey) return false;
            if (item.Media.Type == SubsonicModelTypeEnum.Artist)
            { NavigationService.NavigateToViewModel<NavidromeDetailViewModel>(NavidromeRoute.For("artist", item.Media.Id)); return false; }
            var played = false;
            var api = _api;
            await RunLocalOperation(async () => {
                var songs = await MediaQueue.ResolveAsync(new[] { (ISubsonicModel)item.Media });
                if (!IsCurrent(version, api) || folder != Session.FolderId) return;
                MediaQueue.Play(api, songs); played = songs.Count > 0;
            });
            return played;
        }
    }
}
