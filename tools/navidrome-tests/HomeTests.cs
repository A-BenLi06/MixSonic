using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Client.Common.Models;
using Client.Common.Models.Navidrome;
using Client.Common.Models.Subsonic;
using Client.Common.Services;
using Client.Common.Services.DataStructures.SubsonicService;
using Client.Common.Services.Navidrome;
using Subsonic8.Main;
using Subsonic8.Navidrome;

public static class NavidromeHomeTests
{
    private static int _assertions;
    private static void Check(bool value, string message) { ++_assertions; if (!value) throw new Exception(message); }
    private static void Active(MainViewModel model, bool active)
    { typeof(NavidromePageViewModel).GetField("PageActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(model, active); }
    public static int Main()
    {
        try
        {
            LibraryIndexChecks();
            using (var pump = new Pump()) pump.Run(Run);
            Console.WriteLine("PASS: " + _assertions + " production home/navigation assertions with controlled services and loopback HTTP. Native clicks and layout remain unverified.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void LibraryIndexChecks()
    {
        Check(LibraryIndex.Initial(" 1989") == "#", "Numbers use the symbol index.");
        Check(LibraryIndex.Initial(null) == "#" && LibraryIndex.Initial("  ") == "#", "Missing titles have a valid index.");
        Check(LibraryIndex.Initial("Écho") == "e" && LibraryIndex.Initial("e\u0301cho") == "e", "Composed and combining Latin accents share a group.");
        Check(LibraryIndex.Initial("シネマ") == "シ" && LibraryIndex.Initial("迷跡波") == "迷", "Non-Latin script initials come from metadata.");
        Check(LibraryIndex.Initial("\U0001f3b5 music") == "#", "Emoji use the symbol index without splitting a surrogate.");
        Check(LibraryIndex.Initial("\U00020000 title") == "\U00020000", "Supplementary letters keep their complete initial.");
        var titles = new[] { "Zoo", "1989", "beta", "Alpha", "シネマ", "迷跡波", "Écho" };
        var items = titles.Select((title, number) => new NavidromeItem { Title = title, Media = new Album { Id = number.ToString(), Name = title, Artist = "Artist", ArtistId = "artist", Genre = "Pop" } }).ToList();
        var index = LibraryIndex.Create(items, "albums", "Unknown artist", "Unknown genre");
        Check(index.Groups.Count == 29, "The alphabet includes #, all 26 Latin letters, and discovered script initials.");
        Check(!index.Groups.Single(group => group.Key == "x").IsEnabled, "A truly absent letter is disabled.");
        Check(index.Groups.Single(group => group.Key == "z").IsEnabled, "The last populated letter is enabled.");
        Check(index.Entries.OfType<NavidromeItem>().Count() == items.Count, "Headers do not duplicate or replace playable media.");
        foreach (var group in index.Groups.Where(group => group.IsEnabled))
            Check(ReferenceEquals(index.Entries[index.Entries.IndexOf(group) + 1], group.Items[0]), "An active index targets its exact inline header and following media.");
        var songs = LibraryIndex.Create(items.Select(item => new NavidromeItem { Title = item.Title, Media = new Song { Id = item.Media.Id, Title = item.Title } }), "songs", "", "");
        Check(songs.Groups.All(group => group.IsSong), "Song headers use the compact song-row template.");
        Check(songs.Entries.OfType<NavidromeItem>().Select(item => item.Title).Take(3).SequenceEqual(new[] { "1989", "Alpha", "beta" }), "Playback can follow the displayed song ordering.");
        var artists = LibraryIndex.Create(items, "artists", "Unknown artist", "Unknown genre");
        Check(artists.Groups.Count == 1 && artists.Groups[0].Items.Count == items.Count && artists.Groups[0].IsArtist, "Artist indices contain their actual albums.");
        var shared = (Album)items[0].Media;
        shared.Artists = new List<MediaArtist> { new MediaArtist { Id = "artist", Name = "Artist" }, new MediaArtist { Id = "guest", Name = "Guest" } };
        shared.Genres = new List<MediaGenre> { new MediaGenre { Name = "Pop" }, new MediaGenre { Name = "Rock" }, new MediaGenre { Name = "Rock" } };
        artists = LibraryIndex.Create(items, "artists", "Unknown artist", "Unknown genre");
        Check(artists.Groups.Count == 2 && artists.Groups.Single(group => group.Title == "Guest").Items.Single() == items[0], "Multiple album artists are grouped by their server identifiers.");
        var genres = LibraryIndex.Create(items, "genres", "Unknown artist", "Unknown genre");
        Check(genres.Groups.Count == 2 && genres.Groups.Single(group => group.Title == "Rock").Items.Count == 1, "All metadata genres are indexed without duplicating an album within a genre.");
        foreach (var grouped in new[] { artists, genres })
        {
            Check(grouped.Entries.Distinct().Count() == grouped.Entries.Count, "Every displayed occurrence has a unique identity for XAML automation peers.");
            var occurrences = grouped.Entries.OfType<NavidromeItem>().Where(item => item.SelectionItem == items[0]).ToList();
            Check(occurrences.Count == 2 && occurrences.Select(item => item.LibraryGroupKey).Distinct().Count() == 2,
                "The shared album retains both distinct group occurrences.");
            Check(occurrences.All(item => ReferenceEquals(item.Media, shared) && item.Title == items[0].Title && item.Subtitle == items[0].Subtitle),
                "Occurrences retain shared metadata and canonical selection identity.");
            var rebuilt = LibraryIndex.Create(items, grouped == artists ? "artists" : "genres", "", "");
            Check(rebuilt.Entries.OfType<NavidromeItem>().Count(item => item.SelectionItem == items[0]) == 2,
                "Rebuilding keeps the selected source album mapped to both occurrences.");
        }
        var unknown = new NavidromeItem { Title = "Unknown", Media = new Album { Id = "unknown" } };
        Check(LibraryIndex.Create(new[] { unknown }, "artists", "Fallback artist", "Fallback genre").Groups[0].Title == "Fallback artist", "Unknown artists use the supplied localized label.");
        Check(LibraryIndex.Create(new[] { unknown }, "genres", "Fallback artist", "Fallback genre").Groups[0].Title == "Fallback genre", "Unknown genres use the supplied localized label.");
        var distinctArtists = new[] { "ArtistId", "artistid" }.Select(id => new NavidromeItem { Title = id, Media = new Album { Id = id, ArtistId = id, Artist = "Same name" } });
        Check(LibraryIndex.Create(distinctArtists, "artists", "", "").Groups.Count == 2, "Opaque artist IDs remain case sensitive even when their names match.");
        var filtered = LibraryIndex.Create(items.Take(1), "albums", "", "");
        Check(!filtered.Groups.Single(group => group.Key == "a").IsEnabled && filtered.Groups.Single(group => group.Key == "z").IsEnabled, "Rebuilding after filtering removes stale index targets.");
        var empty = LibraryIndex.Create(Enumerable.Empty<NavidromeItem>(), "albums", "", "");
        Check(empty.Entries.Count == 0 && empty.Groups.All(group => !group.IsEnabled), "An empty library has no clickable header targets.");
    }
    private static async Task WaitFor(Func<bool> predicate)
    {
        for (var i = 0; i < 300 && !predicate(); i++) await Task.Delay(10);
        if (!predicate()) throw new TimeoutException("The expected controlled home request did not arrive.");
    }
    private static async Task Run()
    {
        var failPlaylists = false;
        using (var server = await Task.Run(() => new ProtocolServer(request =>
        {
            var endpoint = request.Uri.AbsolutePath.Split('/').Last().Replace(".view", "");
            var folder = request.Parameter("musicFolderId") ?? "all";
            string body;
            if (endpoint == "getAlbumList2") body = "<albumList2><album id='" + folder + "-" + request.Parameter("type") + "' name='Album &amp; title' artist='Artist' songCount='2'/></albumList2>";
            else if (endpoint == "getArtists") body = "<artists><index name='A'><artist id='artist' name='Artist' albumCount='1'/></index></artists>";
            else if (endpoint == "getPlaylists") body = "<playlists><playlist id='playlist' name='Playlist' songCount='2'/></playlists>";
            else if (endpoint == "getStarred2") body = "<starred2><song id='favourite' title='Favourite' artist='Artist'/></starred2>";
            else throw new InvalidOperationException("Unexpected home endpoint: " + endpoint);
            if (endpoint == "getPlaylists" && failPlaylists)
                return new ProtocolResponse { Body = "<subsonic-response xmlns='http://subsonic.org/restapi' status='failed'><error code='0' message='playlist fixture failure'/></subsonic-response>" };
            return new ProtocolResponse { Body = "<subsonic-response xmlns='http://subsonic.org/restapi' status='ok'>" + body + "</subsonic-response>", DelayMilliseconds = folder == "old" ? 180 : 0 };
        })))
        {
            var session = new Session(server.Url); var navigation = new Navigation(); var offline = new Offline(); var queue = new Queue(); var dialogs = new Dialogs();
            var page = new MainViewModel { Session = session, NavigationService = navigation, Offline = offline, MediaQueue = queue,
                DialogService = dialogs, HomePlayer = new NavidromePlayerViewModel(), EventAggregator = new Events() };
            var expected = new[] { "albums", "artists", "songs", "genres", "playlists", "random", "favourites", "radio", "activity", "bookmarks", "shares", "downloads", "account" };
            Check(page.Destinations.Select(item => item.Route).OrderBy(item => item).SequenceEqual(expected.OrderBy(item => item)), "All supported home destinations remain reachable.");
            foreach (var route in expected)
            {
                await page.OpenTile(page.Destinations.Single(item => item.Route == route));
                var type = route == "downloads" ? typeof(NavidromeDownloadsViewModel) : route == "account" ? typeof(NavidromeAccountViewModel) : typeof(NavidromeBrowserViewModel);
                Check(navigation.Page == type && (type != typeof(NavidromeBrowserViewModel) || Equals(navigation.Parameter, route)), "Home route reaches its actual view model: " + route);
            }
            page.Settings(); Check(dialogs.Opened == 1, "Connection settings opens through the home controller.");
            page.OpenPlayer(); Check(navigation.Page == typeof(NavidromePlayerViewModel), "The full player is reachable from home.");
            page.PlayMe(); Check(Equals(navigation.Parameter, "random"), "Random music remains reachable.");
            var savedState = new Dictionary<string, object>();
            page.SaveState(savedState, new List<Type>()); page.LoadState(null, savedState);
            Check(ReferenceEquals(page.Player.Saved, savedState) && ReferenceEquals(page.Player.Loaded, savedState), "The home page forwards suspension state to its live player.");

            Active(page, true);
            offline.Entries.Add(Download(session.AccountKey, "one")); offline.Entries.Add(Download(session.AccountKey, "two"));
            offline.Entries.Add(Download("another account", "foreign"));
            session.Offline = true; await page.Reload();
            Check(page.HasError && !page.IsLoading, "An unreachable server leaves an actionable error and ends the loading state.");
            Check(page.Downloads.Items.OfType<NavidromeItem>().Count() == 2, "The home page loads only the current account's offline music before connecting.");
            foreach (var section in Sections(page))
                Check(section.Items.Last() is LibraryDestination, "Every section retains its See all destination when offline.");
            var downloaded = page.Downloads.Items.OfType<NavidromeItem>().Last();
            Check(await page.OpenTile(downloaded) && queue.LocalIndex == 1 && queue.Local.Count == 2, "An offline tile starts its selected occurrence within the available preview queue.");
            var orphan = new NavidromeItem { Download = Download(session.AccountKey, "gone") };
            Check(!await page.OpenTile(orphan), "A removed download cannot start an unrelated first track.");

            session.Offline = false; await page.Reload();
            Check(!page.HasError && Sections(page).All(section => !section.IsLoading && section.HasMusic), "All home sections populate using production Navidrome parsing.");
            Check(page.Playlists.Items.OfType<NavidromeItem>().First().IsHero, "The playlist mosaic has one featured first entry.");
            var album = page.MyMusic.Items.OfType<NavidromeItem>().First();
            Check(album.Title == "Album & title" && album.Media.Id.EndsWith("newest"), "The album preview uses actual decoded server metadata and the correct listing.");
            Check(await page.OpenTile(album) && queue.Played == 1 && queue.Resolved.Single() == album.Media.Id, "An album tile resolves and plays its actual media context.");
            Check(!await page.OpenTile(page.Artists.Items.OfType<NavidromeItem>().First()) && navigation.Page == typeof(NavidromeDetailViewModel) && Equals(navigation.Parameter, "artist|artist"), "An artist tile opens the artist's detail page.");

            failPlaylists = true; await page.Reload();
            Check(page.Playlists.Error.Contains("playlist fixture failure") && page.MyMusic.HasMusic && !page.HasError, "A failed preview is isolated from successful home sections.");
            Check(page.Playlists.Items.Count == 1 && page.Playlists.Items[0] is LibraryDestination, "A failed preview still has a working See all tile.");
            failPlaylists = false;
            session.Folder = "old"; var stale = page.Reload();
            await WaitFor(() => server.Requests.Any(request => request.Parameter("musicFolderId") == "old"));
            session.Folder = "new"; await page.Reload(); await stale;
            Check(page.MyMusic.Items.OfType<NavidromeItem>().Single().Media.Id == "new-newest", "Late responses cannot replace the selected library's home preview.");

            var pending = new TaskCompletionSource<IList<Song>>(); queue.Pending = pending.Task;
            var play = page.OpenTile(page.MyMusic.Items.OfType<NavidromeItem>().First());
            Active(page, false); pending.SetResult(new List<Song> { new Song { Id = "late", Title = "Late" } });
            Check(!await play && queue.Played == 1, "Leaving home cancels an unfinished tile playback request.");
            Active(page, true); queue.Pending = null;
            pending = new TaskCompletionSource<IList<Song>>(); queue.Pending = pending.Task;
            play = page.OpenTile(page.MyMusic.Items.OfType<NavidromeItem>().First());
            session.Configuration.Username = "changed"; pending.SetResult(new List<Song> { new Song { Id = "foreign" } });
            Check(!await play && queue.Played == 1, "Changing accounts cancels an unfinished online tile request.");

            var templates = new MixRadioHomeTemplateSelector { AlbumTemplate = new Windows.UI.Xaml.DataTemplate(), PlaylistTemplate = new Windows.UI.Xaml.DataTemplate(), LinkTemplate = new Windows.UI.Xaml.DataTemplate() };
            var featured = new NavidromeItem { IsHero = true, Media = new Playlist() };
            Check(templates.Select(featured) == templates.PlaylistTemplate, "A compact home panel uses a small playlist template for the featured entry.");
            templates.HeroTemplate = new Windows.UI.Xaml.DataTemplate();
            Check(templates.Select(featured) == templates.HeroTemplate, "The wide mosaic uses the original large template.");
            Check(templates.Select(page.MyMusic.Destination) == templates.LinkTemplate, "See all is presented as navigation, not a playable media item.");
        }
    }
    private static IEnumerable<HomeSection> Sections(MainViewModel page) { return new[] { page.Recent, page.Playlists, page.Artists, page.Downloads, page.Favourites, page.MyMusic }; }
    private static OfflineDownloadItem Download(string account, string id)
    {
        var song = new Song { Id = id, Title = id, Artist = "Fixture", Duration = 30 };
        return new OfflineDownloadItem { AccountKey = account, Song = song, PlaybackAvailable = true, Record = new DownloadedSong { Song = song } };
    }
    private sealed class Session : PropertyChangedBase, INavidromeSessionService
    {
        public Session(string url) { Configuration = new SubsonicServiceConfiguration { BaseUrl = url, Username = "home-fixture", Password = "fixture" }; Folders = new BindableCollection<MusicFolder>(); }
        public SubsonicServiceConfiguration Configuration;
        public bool Offline; public string Folder;
        public event EventHandler LibraryChanged { add { } remove { } }
        public ServerCapabilities Capabilities { get { return new ServerCapabilities(); } }
        public ServerUser User { get { return new ServerUser(); } }
        public BindableCollection<MusicFolder> Folders { get; private set; }
        public MusicFolder SelectedFolder { get; set; }
        public string FolderId { get { return Folder; } }
        public string AccountKey { get { return AccountIdentity.For(Configuration); } }
        public string Status { get { return null; } }
        public string CapabilityError { get { return null; } }
        public string UserError { get { return null; } }
        public bool IsConnected { get { return !Offline; } }
        public Task ConnectAsync(bool refresh = false) { if (Offline) throw new InvalidOperationException("Controlled offline fixture"); return Task.FromResult(true); }
        public NavidromeApi CreateApi() { return new NavidromeApi(Configuration); }
    }
    private sealed class Navigation : INavigationService
    {
        public Type Page; public object Parameter;
        public void Navigate(Type page, object parameter) { Page = page; Parameter = parameter; }
    }
    private sealed class Queue : IMediaQueueService
    {
        public List<string> Resolved; public int Played; public int LocalIndex; public List<PlaylistItem> Local;
        public Task<IList<Song>> Pending;
        public Task<IList<Song>> ResolveAsync(IEnumerable<ISubsonicModel> items)
        { Resolved = items.Select(item => item.Id).ToList(); return Pending ?? Task.FromResult<IList<Song>>(new List<Song> { new Song { Id = "resolved", Title = "Song" } }); }
        public void Play(NavidromeApi api, IEnumerable<Song> songs, int index = 0, long positionMilliseconds = 0) { Played++; }
        public void PlayLocal(IEnumerable<PlaylistItem> songs, int index = 0) { Local = songs.ToList(); LocalIndex = index; }
    }
    private sealed class Offline : IOfflineLibraryService
    {
        public BindableCollection<OfflineDownloadItem> Entries { get; private set; }
        public Offline() { Entries = new BindableCollection<OfflineDownloadItem>(); }
        public Task RefreshAsync() { return Task.FromResult(true); }
        public Task<IList<PlaylistItem>> CreateQueueAsync(IEnumerable<OfflineDownloadItem> items)
        { return Task.FromResult<IList<PlaylistItem>>(items.Select(item => new PlaylistItem { SongId = item.Song.Id, AccountKey = item.AccountKey }).ToList()); }
    }
    private sealed class Dialogs : Subsonic8.Framework.Services.IDialogService
    { public int Opened; public void ShowSettings<T>() where T : Screen { ++Opened; } }
    private sealed class Events : IEventAggregator
    {
        public bool HandlerExistsFor(Type type) { return false; }
        public void Subscribe(object target) { }
        public void Unsubscribe(object target) { }
        public void Publish(object message, Action<Action> marshal) { }
    }
    private sealed class Pump : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<Action> _work = new BlockingCollection<Action>();
        public override void Post(SendOrPostCallback callback, object state) { _work.Add(() => callback(state)); }
        public void Run(Func<Task> work)
        {
            SetSynchronizationContext(this); var task = work();
            while (!task.IsCompleted) { Action next; if (_work.TryTake(out next, 100)) next(); }
            task.GetAwaiter().GetResult(); SetSynchronizationContext(null);
        }
        public void Dispose() { _work.Dispose(); }
    }
}
