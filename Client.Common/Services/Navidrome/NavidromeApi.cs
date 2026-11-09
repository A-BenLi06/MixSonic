namespace Client.Common.Services.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Xml.Linq;
    using Client.Common.Models;
    using Client.Common.Models.Navidrome;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using Client.Common.Services.DataStructures.SubsonicService;

    public sealed class NavidromeApi
    {
        private readonly SubsonicServiceConfiguration _configuration;
        private static readonly XNamespace Ns = SubsonicXml.Namespace;

        public NavidromeApi(ISubsonicServiceConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");
            var streaming = configuration as SubsonicServiceConfiguration;
            _configuration = new SubsonicServiceConfiguration
            {
                BaseUrl = configuration.BaseUrl, Username = configuration.Username, Password = configuration.Password,
                CompatibleMode = configuration.CompatibleMode, PrimaryUrl = configuration.PrimaryUrl, SecondaryUrl = configuration.SecondaryUrl,
                AuthenticationMode = streaming == null ? SubsonicAuthenticationMode.Password : streaming.AuthenticationMode,
                StreamingFormat = streaming == null ? null : streaming.StreamingFormat,
                MaximumBitRate = streaming == null ? 0 : streaming.MaximumBitRate
            };
            AccountKey = AccountIdentity.For(_configuration);
            UseFormPost = true;
        }

        public string AccountKey { get; private set; }
        public bool UseFormPost { get; set; }
        private SubsonicRequest Request(string endpoint) { return new SubsonicRequest(_configuration, endpoint); }
        private NavidromeResult<T> Read<T>(SubsonicRequest request, Func<XElement, T> parser)
        { return new NavidromeResult<T>(_configuration, request, parser); }
        private NavidromeResult<bool> Write(SubsonicRequest request)
        { return new NavidromeResult<bool>(_configuration, request, root => true, UseFormPost); }
        private static string Id(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A server item ID is required.", "id");
            return id;
        }
        private static void Page(int offset, int count)
        {
            if (offset < 0 || count < 0 || count > 500) throw new ArgumentOutOfRangeException("offset/count");
        }

        public NavidromeResult<bool> Ping() { return Read(Request("ping"), root => true); }

        public NavidromeResult<string> GetTokenInfo()
        {
            return Read(Request("tokenInfo"), root =>
            {
                var token = root.Element(Ns + "tokenInfo");
                var username = token == null ? null : SubsonicXml.Attribute(token, "username");
                if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Token identity is missing.");
                return username;
            });
        }

        public NavidromeResult<IDictionary<string, IList<int>>> GetExtensions()
        {
            return Read(Request("getOpenSubsonicExtensions"), root =>
            {
                var result = new Dictionary<string, IList<int>>(StringComparer.Ordinal);
                foreach (var extension in root.Elements(Ns + "openSubsonicExtensions"))
                {
                    var name = (string)extension.Attribute("name");
                    if (!string.IsNullOrEmpty(name)) result[name] = extension.Elements(Ns + "versions").Select(v => (int)v).ToList();
                }
                return (IDictionary<string, IList<int>>)result;
            });
        }

        public NavidromeResult<ServerUser> GetUser()
        {
            return Read(Request("getUser").Add("username", _configuration.Username), root =>
            {
                var user = root.Element(Ns + "user");
                if (user == null) throw new InvalidOperationException("User response is missing.");
                return new ServerUser
                {
                    Username = SubsonicXml.Attribute(user, "username"), AdminRole = SubsonicXml.Bool(user, "adminRole"),
                    DownloadRole = SubsonicXml.Bool(user, "downloadRole"), ShareRole = SubsonicXml.Bool(user, "shareRole"),
                    StreamRole = SubsonicXml.Bool(user, "streamRole"), ScrobblingEnabled = SubsonicXml.Bool(user, "scrobblingEnabled"),
                    JukeboxRole = SubsonicXml.Bool(user, "jukeboxRole")
                };
            });
        }

        public NavidromeResult<IList<MusicFolder>> GetMusicFolders()
        { return Read(Request("getMusicFolders"), root => SubsonicXml.List<MusicFolder>(root.Element(Ns + "musicFolders"), "musicFolder")); }

        public NavidromeResult<IList<ExpandedArtist>> GetArtists(string folderId)
        {
            return Read(Request("getArtists").Add("musicFolderId", folderId), root =>
            {
                var artists = root.Element(Ns + "artists");
                return (IList<ExpandedArtist>)(artists == null ? new List<ExpandedArtist>() : artists.Descendants(Ns + "artist").Select(SubsonicXml.Read<ExpandedArtist>).ToList());
            });
        }

        public NavidromeResult<IList<Album>> GetAlbums(AlbumQuery query)
        {
            Page(query.Offset, query.Size);
            var request = Request("getAlbumList2").Add("type", query.ApiType).Add("offset", query.Offset).Add("size", query.Size)
                .Add("musicFolderId", query.MusicFolderId);
            if (query.Type == AlbumListType.ByGenre) request.Add("genre", query.Genre ?? string.Empty);
            if (query.Type == AlbumListType.ByYear) request.Add("fromYear", query.FromYear).Add("toYear", query.ToYear);
            return Read(request, root => SubsonicXml.List<Album>(root.Element(Ns + "albumList2"), "album"));
        }

        public NavidromeResult<SearchResultCollection> Search(SearchQuery query)
        {
            Page(query.ArtistOffset, query.ArtistCount); Page(query.AlbumOffset, query.AlbumCount); Page(query.SongOffset, query.SongCount);
            var request = Request("search3").Add("query", query.Query ?? string.Empty).Add("musicFolderId", query.MusicFolderId)
                .Add("artistCount", query.ArtistCount).Add("artistOffset", query.ArtistOffset)
                .Add("albumCount", query.AlbumCount).Add("albumOffset", query.AlbumOffset)
                .Add("songCount", query.SongCount).Add("songOffset", query.SongOffset);
            return Read(request, root =>
            {
                var result = SubsonicXml.Read<SearchResultCollection>(root.Element(Ns + "searchResult3")) ?? new SearchResultCollection();
                result.Query = query.Query;
                return result;
            });
        }

        public NavidromeResult<Album> GetAlbum(string id)
        { return Read(Request("getAlbum").Add("id", Id(id)), root => SubsonicXml.Read<Album>(root.Element(Ns + "album"))); }
        public NavidromeResult<ExpandedArtist> GetArtist(string id)
        { return Read(Request("getArtist").Add("id", Id(id)), root => SubsonicXml.Read<ExpandedArtist>(root.Element(Ns + "artist"))); }
        public NavidromeResult<Song> GetSong(string id)
        { return Read(Request("getSong").Add("id", Id(id)), root => SubsonicXml.Read<Song>(root.Element(Ns + "song"))); }
        public NavidromeResult<IList<Genre>> GetGenres(string folderId)
        { return Read(Request("getGenres").Add("musicFolderId", folderId), root => SubsonicXml.List<Genre>(root.Element(Ns + "genres"), "genre")); }
        public NavidromeResult<IList<Song>> GetGenreSongs(string genre, string folderId, int offset, int count)
        {
            Page(offset, count);
            return Read(Request("getSongsByGenre").Add("genre", genre).Add("musicFolderId", folderId).Add("offset", offset).Add("count", count),
                root => SubsonicXml.List<Song>(root.Element(Ns + "songsByGenre"), "song"));
        }
        public NavidromeResult<IList<Song>> GetRandomSongs(string folderId, int count = 100)
        { return GetRandomSongs(new RandomSongQuery { MusicFolderId = folderId, Size = count }); }

        public NavidromeResult<IList<Song>> GetRandomSongs(RandomSongQuery query)
        {
            if (query == null) throw new ArgumentNullException("query");
            if (query.Size < 1 || query.Size > 500) throw new ArgumentOutOfRangeException("query", "The random selection size must be between 1 and 500.");
            if (query.FromYear.HasValue && query.FromYear.Value < 0 || query.ToYear.HasValue && query.ToYear.Value < 0 ||
                query.FromYear.HasValue && query.ToYear.HasValue && query.FromYear.Value > query.ToYear.Value)
                throw new ArgumentOutOfRangeException("query", "The random selection year range is invalid.");
            var request = Request("getRandomSongs").Add("musicFolderId", query.MusicFolderId).Add("size", query.Size)
                .Add("genre", string.IsNullOrWhiteSpace(query.Genre) ? null : query.Genre);
            if (query.FromYear.HasValue) request.Add("fromYear", query.FromYear.Value);
            if (query.ToYear.HasValue) request.Add("toYear", query.ToYear.Value);
            return Read(request,
                root => SubsonicXml.List<Song>(root.Element(Ns + "randomSongs"), "song"));
        }

        public NavidromeResult<StarredMedia> GetStarred(string folderId)
        {
            return Read(Request("getStarred2").Add("musicFolderId", folderId), root =>
            {
                var starred = root.Element(Ns + "starred2");
                return new StarredMedia { Artists = SubsonicXml.List<ExpandedArtist>(starred, "artist"),
                    Albums = SubsonicXml.List<Album>(starred, "album"), Songs = SubsonicXml.List<Song>(starred, "song") };
            });
        }

        public NavidromeResult<bool> SetStarred(ISubsonicModel item, bool value)
        {
            var key = item.Type == SubsonicModelTypeEnum.Album ? "albumId" : item.Type == SubsonicModelTypeEnum.Artist ? "artistId" : "id";
            return Write(Request(value ? "star" : "unstar").Add(key, Id(item.Id)));
        }
        public NavidromeResult<bool> SetRating(string id, int rating)
        {
            if (rating < 0 || rating > 5) throw new ArgumentOutOfRangeException("rating");
            return Write(Request("setRating").Add("id", Id(id)).Add("rating", rating));
        }

        public NavidromeResult<ArtistInformation> GetArtistInfo(string id)
        {
            return Read(Request("getArtistInfo2").Add("id", Id(id)).Add("includeNotPresent", false), root =>
            {
                var info = root.Element(Ns + "artistInfo2");
                if (info == null) return new ArtistInformation();
                return new ArtistInformation { Biography = SubsonicXml.Text(info, "biography"), MusicBrainzId = SubsonicXml.Text(info, "musicBrainzId"),
                    LastFmUrl = SubsonicXml.Text(info, "lastFmUrl"), ImageUrl = SubsonicXml.Text(info, "largeImageUrl"),
                    SimilarArtists = SubsonicXml.List<ExpandedArtist>(info, "similarArtist").Where(a => !string.IsNullOrEmpty(a.Id) && a.Id != "-1").ToList() };
            });
        }
        public NavidromeResult<AlbumInformation> GetAlbumInfo(string id)
        {
            return Read(Request("getAlbumInfo2").Add("id", Id(id)), root =>
            {
                var info = root.Element(Ns + "albumInfo");
                if (info == null) return new AlbumInformation();
                return new AlbumInformation { Notes = SubsonicXml.Text(info, "notes"), MusicBrainzId = SubsonicXml.Text(info, "musicBrainzId"),
                    LastFmUrl = SubsonicXml.Text(info, "lastFmUrl"), ImageUrl = SubsonicXml.Text(info, "largeImageUrl") };
            });
        }
        public NavidromeResult<IList<Song>> GetSimilarSongs(string id, int count = 50)
        { return Read(Request("getSimilarSongs2").Add("id", Id(id)).Add("count", count), root => SubsonicXml.List<Song>(root.Element(Ns + "similarSongs2"), "song")); }
        public NavidromeResult<IList<Song>> GetTopSongs(string artistName, string artistId = null, int count = 50)
        { return Read(Request("getTopSongs").Add("artist", artistName).Add("id", artistId).Add("count", count), root => SubsonicXml.List<Song>(root.Element(Ns + "topSongs"), "song")); }

        public NavidromeResult<IList<LyricVariant>> GetLyrics(string id, bool enhanced)
        {
            return Read(Request("getLyricsBySongId").Add("id", Id(id)).Add("enhanced", enhanced), root =>
            {
                var list = root.Element(Ns + "lyricsList");
                return (IList<LyricVariant>)(list == null ? new List<LyricVariant>() : list.Elements(Ns + "structuredLyrics").Select(lyric => new LyricVariant
                {
                    Language = SubsonicXml.Attribute(lyric, "lang"), Kind = SubsonicXml.Attribute(lyric, "kind"),
                    DisplayTitle = SubsonicXml.Attribute(lyric, "displayTitle"), DisplayArtist = SubsonicXml.Attribute(lyric, "displayArtist"),
                    Synced = SubsonicXml.Bool(lyric, "synced"), OffsetMilliseconds = SubsonicXml.Long(lyric, "offset"),
                    Lines = lyric.Elements(Ns + "line").Select(line => new LyricsLine { StartMilliseconds = (long?)line.Attribute("start"), Text = line.Value }).ToList()
                }).ToList());
            });
        }
        public NavidromeResult<IList<LyricVariant>> GetLegacyLyrics(string artist, string title)
        {
            return Read(Request("getLyrics").Add("artist", artist).Add("title", title), root =>
            {
                var lyrics = root.Element(Ns + "lyrics");
                return (IList<LyricVariant>)(lyrics == null || string.IsNullOrWhiteSpace(lyrics.Value) ? new List<LyricVariant>() : new List<LyricVariant>
                {
                    new LyricVariant { DisplayArtist = artist, DisplayTitle = title,
                        Lines = lyrics.Value.Split(new[] { '\n' }).Select(line => new LyricsLine { Text = line.TrimEnd('\r') }).ToList() }
                });
            });
        }

        public NavidromeResult<ServerPlayQueue> GetQueue(bool indexBased)
        {
            var name = indexBased ? "playQueueByIndex" : "playQueue";
            return Read(Request(indexBased ? "getPlayQueueByIndex" : "getPlayQueue"), root =>
            {
                var queue = root.Element(Ns + name);
                if (queue == null) throw new InvalidOperationException("Play queue response is missing.");
                var songs = SubsonicXml.List<Song>(queue, "entry");
                var current = indexBased ? (int?)queue.Attribute("currentIndex") ?? (songs.Count == 0 ? -1 : 0)
                    : songs.ToList().FindIndex(song => song.Id == (string)queue.Attribute("current"));
                if (songs.Count > 0 && (current < 0 || current >= songs.Count)) throw new InvalidOperationException("Play queue index is invalid.");
                return new ServerPlayQueue { Songs = songs, CurrentIndex = current, PositionMilliseconds = SubsonicXml.Long(queue, "position"),
                    ChangedBy = SubsonicXml.Attribute(queue, "changedBy"), Changed = SubsonicXml.Attribute(queue, "changed") };
            });
        }
        public NavidromeResult<bool> SaveQueue(IEnumerable<string> songIds, int currentIndex, long positionMilliseconds, bool indexBased)
        {
            var ids = songIds.Select(Id).ToList();
            if (positionMilliseconds < 0 || (ids.Count > 0 && (currentIndex < 0 || currentIndex >= ids.Count))) throw new ArgumentOutOfRangeException("currentIndex/positionMilliseconds");
            var request = Request(indexBased ? "savePlayQueueByIndex" : "savePlayQueue").AddMany("id", ids).Add("position", positionMilliseconds);
            if (ids.Count > 0)
            {
                if (indexBased) request.Add("currentIndex", currentIndex);
                else request.Add("current", ids[currentIndex]);
            }
            return Write(request);
        }

        public NavidromeResult<IList<Playlist>> GetPlaylists()
        { return Read(Request("getPlaylists"), root => SubsonicXml.List<Playlist>(root.Element(Ns + "playlists"), "playlist")); }
        public NavidromeResult<Playlist> GetPlaylist(string id)
        { return Read(Request("getPlaylist").Add("id", Id(id)), root => SubsonicXml.Read<Playlist>(root.Element(Ns + "playlist"))); }
        public NavidromeResult<Playlist> SavePlaylist(string name, IEnumerable<string> songs, string playlistId = null)
        {
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrEmpty(playlistId)) throw new ArgumentException("A playlist name is required.", "name");
            var request = Request("createPlaylist").Add("name", name).Add("playlistId", playlistId).AddMany("songId", songs.Select(Id));
            return new NavidromeResult<Playlist>(_configuration, request, root => SubsonicXml.Read<Playlist>(root.Element(Ns + "playlist")), UseFormPost);
        }
        public NavidromeResult<bool> UpdatePlaylist(string id, string name, string comment, bool? isPublic, IEnumerable<string> add, IEnumerable<int> remove)
        {
            var request = Request("updatePlaylist").Add("playlistId", Id(id)).Add("name", name).Add("comment", comment).AddMany("songIdToAdd", add);
            if (isPublic.HasValue) request.Add("public", isPublic.Value);
            if (remove != null) foreach (var index in remove)
            {
                if (index < 0) throw new ArgumentOutOfRangeException("remove");
                request.Add("songIndexToRemove", index);
            }
            return Write(request);
        }
        public NavidromeResult<bool> DeletePlaylist(string id) { return Write(Request("deletePlaylist").Add("id", Id(id))); }

        public NavidromeResult<IList<Bookmark>> GetBookmarks()
        {
            return Read(Request("getBookmarks"), root =>
            {
                var bookmarks = root.Element(Ns + "bookmarks");
                return (IList<Bookmark>)(bookmarks == null ? new List<Bookmark>() : bookmarks.Elements(Ns + "bookmark").Select(bookmark => new Bookmark
                {
                    Song = SubsonicXml.Read<Song>(bookmark.Element(Ns + "entry")), PositionMilliseconds = SubsonicXml.Long(bookmark, "position"),
                    Comment = SubsonicXml.Attribute(bookmark, "comment")
                }).Where(bookmark => bookmark.Song != null).ToList());
            });
        }
        public NavidromeResult<bool> SaveBookmark(string id, long positionMilliseconds, string comment)
        {
            if (positionMilliseconds < 0) throw new ArgumentOutOfRangeException("positionMilliseconds");
            return Write(Request("createBookmark").Add("id", Id(id)).Add("position", positionMilliseconds).Add("comment", comment));
        }
        public NavidromeResult<bool> DeleteBookmark(string id) { return Write(Request("deleteBookmark").Add("id", Id(id))); }

        private static IList<Share> ReadShares(XElement root)
        {
            var shares = root.Element(Ns + "shares");
            return shares == null ? new List<Share>() : shares.Elements(Ns + "share").Select(share =>
            {
                DateTimeOffset expiry;
                var hasExpiry = DateTimeOffset.TryParse((string)share.Attribute("expires"), out expiry) && UnixMilliseconds(expiry) > 0;
                return new Share { Id = SubsonicXml.Attribute(share, "id"), Url = SubsonicXml.Attribute(share, "url"),
                    Description = SubsonicXml.Attribute(share, "description"), Username = SubsonicXml.Attribute(share, "username"),
                    Expires = hasExpiry ? (DateTimeOffset?)expiry : null, VisitCount = (int)SubsonicXml.Long(share, "visitCount") };
            }).ToList();
        }
        public NavidromeResult<IList<Share>> GetShares() { return Read(Request("getShares"), ReadShares); }
        public NavidromeResult<IList<Share>> CreateShare(IEnumerable<string> ids, string description, DateTimeOffset? expires)
        {
            var request = Request("createShare").AddMany("id", ids.Select(Id)).Add("description", description);
            if (expires.HasValue) request.Add("expires", UnixMilliseconds(expires.Value));
            return new NavidromeResult<IList<Share>>(_configuration, request, ReadShares, UseFormPost);
        }
        public NavidromeResult<bool> UpdateShare(string id, string description, DateTimeOffset? expires)
        {
            var request = Request("updateShare").Add("id", Id(id)).Add("description", description);
            // Navidrome preserves the existing expiry when it is omitted (or zero).
            if (expires.HasValue) request.Add("expires", UnixMilliseconds(expires.Value));
            return Write(request);
        }
        public NavidromeResult<bool> DeleteShare(string id) { return Write(Request("deleteShare").Add("id", Id(id))); }

        public NavidromeResult<ScanStatus> GetScanStatus(bool start = false)
        {
            return Read(Request(start ? "startScan" : "getScanStatus"), root =>
            {
                var scan = root.Element(Ns + "scanStatus");
                if (scan == null) throw new InvalidOperationException("Scan status response is missing.");
                return new ScanStatus { Scanning = SubsonicXml.Bool(scan, "scanning"), Count = SubsonicXml.Long(scan, "count"), LastScan = SubsonicXml.Attribute(scan, "lastScan") };
            });
        }
        public NavidromeResult<IList<InternetRadioStation>> GetRadio()
        { return Read(Request("getInternetRadioStations"), root => SubsonicXml.List<InternetRadioStation>(root.Element(Ns + "internetRadioStations"), "internetRadioStation")); }
        public NavidromeResult<bool> SaveRadio(string id, string name, string streamUrl, string homepageUrl)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A station name is required.", "name");
            if (string.IsNullOrWhiteSpace(streamUrl)) throw new ArgumentException("A stream address is required.", "streamUrl");
            var request = Request(string.IsNullOrEmpty(id) ? "createInternetRadioStation" : "updateInternetRadioStation").Add("name", name).Add("streamUrl", streamUrl).Add("homepageUrl", homepageUrl);
            if (!string.IsNullOrEmpty(id)) request.Add("id", Id(id));
            return Write(request);
        }
        public NavidromeResult<bool> DeleteRadio(string id) { return Write(Request("deleteInternetRadioStation").Add("id", Id(id))); }
        public NavidromeResult<IList<Song>> GetSonicSimilarSongs(string id, int count = 25)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException("count");
            return Read(Request("getSonicSimilarTracks").Add("id", Id(id)).Add("count", count), ReadSonicSongs);
        }
        public NavidromeResult<IList<Song>> FindSonicPath(string startSongId, string endSongId, int count = 25)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException("count");
            return Read(Request("findSonicPath").Add("startSongId", Id(startSongId)).Add("endSongId", Id(endSongId)).Add("count", count), ReadSonicSongs);
        }
        private static IList<Song> ReadSonicSongs(XElement root)
        {
            var songs = root.Elements(Ns + "sonicMatch").Select(match => SubsonicXml.Read<Song>(match.Element(Ns + "entry"))).ToList();
            if (songs.Any(song => song == null)) throw new InvalidOperationException("Sonic match entry is missing.");
            return songs;
        }
        public NavidromeResult<JukeboxState> Jukebox(JukeboxAction action, IEnumerable<string> ids = null, int? index = null, int? offset = null, double? gain = null)
        {
            if (!Enum.IsDefined(typeof(JukeboxAction), action)) throw new ArgumentOutOfRangeException("action");
            if ((action == JukeboxAction.Skip || action == JukeboxAction.Remove) && (!index.HasValue || index.Value < 0)) throw new ArgumentOutOfRangeException("index");
            if (offset.HasValue && offset.Value < 0) throw new ArgumentOutOfRangeException("offset");
            if (action == JukeboxAction.SetGain && (!gain.HasValue || double.IsNaN(gain.Value) || double.IsInfinity(gain.Value) || gain.Value < 0 || gain.Value > 1)) throw new ArgumentOutOfRangeException("gain");
            var request = Request("jukeboxControl").Add("action", action == JukeboxAction.SetGain ? "setGain" : action.ToString().ToLowerInvariant());
            if (ids != null) request.AddMany("id", ids.Select(Id));
            if (index.HasValue) request.Add("index", index.Value);
            if (offset.HasValue) request.Add("offset", offset.Value);
            if (gain.HasValue) request.Add("gain", gain.Value);
            Func<XElement, JukeboxState> read = root =>
            {
                var data = root.Element(Ns + "jukeboxPlaylist") ?? root.Element(Ns + "jukeboxStatus");
                if (data == null) throw new InvalidOperationException("Jukebox status response is missing.");
                var state = new JukeboxState { Playing = SubsonicXml.Bool(data, "playing"), CurrentIndex = (int?)data.Attribute("currentIndex") ?? -1,
                    Gain = (double?)data.Attribute("gain") ?? 0, Position = (int?)data.Attribute("position") ?? 0, Songs = SubsonicXml.List<Song>(data, "entry") };
                if (double.IsNaN(state.Gain) || double.IsInfinity(state.Gain) || state.Gain < 0 || state.Gain > 1 || state.Position < 0)
                    throw new InvalidOperationException("Invalid jukebox playback status.");
                return state;
            };
            return new NavidromeResult<JukeboxState>(_configuration, request, read, UseFormPost && action != JukeboxAction.Get && action != JukeboxAction.Status);
        }
        public NavidromeResult<IList<NowPlayingEntry>> GetNowPlaying()
        { return Read(Request("getNowPlaying"), root => SubsonicXml.List<NowPlayingEntry>(root.Element(Ns + "nowPlaying"), "entry")); }

        public NavidromeResult<bool> ReportPlayback(string id, long positionMilliseconds, string state, double rate, string clientId, bool ignoreScrobble = false)
        {
            if (positionMilliseconds < 0 || double.IsNaN(rate) || double.IsInfinity(rate) || rate <= 0) throw new ArgumentOutOfRangeException("positionMilliseconds/rate");
            if (state != "starting" && state != "playing" && state != "paused" && state != "stopped") throw new ArgumentException("Invalid playback state.", "state");
            return Write(Request("reportPlayback").Add("mediaId", Id(id)).Add("mediaType", "song").Add("positionMs", positionMilliseconds)
                .Add("state", state).Add("playbackRate", rate).Add("clientId", clientId).Add("ignoreScrobble", ignoreScrobble));
        }
        public NavidromeResult<bool> Scrobble(string id, bool submission, DateTimeOffset startedAt, int positionSeconds)
        {
            return Write(Request("scrobble").Add("id", Id(id)).Add("submission", submission).Add("time", UnixMilliseconds(startedAt)).Add("position", positionSeconds));
        }
        public static long UnixMilliseconds(DateTimeOffset timestamp)
        { return (long)(timestamp.ToUniversalTime() - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalMilliseconds; }

        public Uri DownloadUri(string id) { return new Uri(Request("download").Add("id", Id(id)).Url); }

        public NavidromeResult<TranscodeDecision> GetTranscodeDecision(string id, ClientPlaybackProfile profile, CancellationToken cancellation = default(CancellationToken))
        {
            if (profile == null) throw new ArgumentNullException("profile");
            return new NavidromeResult<TranscodeDecision>(_configuration, Request("getTranscodeDecision").Add("mediaId", Id(id)).Add("mediaType", "song"), root =>
            {
                var decision = root.Element(Ns + "transcodeDecision");
                if (decision == null) throw new InvalidOperationException("The transcode decision is missing.");
                return new TranscodeDecision { CanDirectPlay = SubsonicXml.Bool(decision, "canDirectPlay"), CanTranscode = SubsonicXml.Bool(decision, "canTranscode"),
                    TranscodeParams = SubsonicXml.Attribute(decision, "transcodeParams"), ErrorReason = SubsonicXml.Attribute(decision, "errorReason"),
                    Reasons = decision.Elements(Ns + "transcodeReason").Select(element => element.Value).ToList(),
                    SourceStream = ReadStreamDetails(decision.Element(Ns + "sourceStream")), TranscodeStream = ReadStreamDetails(decision.Element(Ns + "transcodeStream")) };
            }, json: profile.ToJson(), cancellation: cancellation);
        }

        private static AudioStreamDetails ReadStreamDetails(XElement stream)
        {
            return stream == null ? null : new AudioStreamDetails { Protocol = SubsonicXml.Attribute(stream, "protocol"), Container = SubsonicXml.Attribute(stream, "container"),
                Codec = SubsonicXml.Attribute(stream, "codec"), Bitrate = SubsonicXml.Long(stream, "audioBitrate"), Channels = (int)SubsonicXml.Long(stream, "audioChannels"),
                SampleRate = (int)SubsonicXml.Long(stream, "audioSamplerate"), BitDepth = (int)SubsonicXml.Long(stream, "audioBitdepth") };
        }

        public Uri TranscodeStreamUri(string id, string transcodeParams, int offsetSeconds = 0)
        {
            if (string.IsNullOrWhiteSpace(transcodeParams)) throw new ArgumentException("Transcode parameters are required.", "transcodeParams");
            if (offsetSeconds < 0) throw new ArgumentOutOfRangeException("offsetSeconds");
            return new Uri(Request("getTranscodeStream").Add("mediaId", Id(id)).Add("mediaType", "song").Add("transcodeParams", transcodeParams).Add("offset", offsetSeconds).Url);
        }

        public Uri StreamUri(string id, string format)
        {
            var request = Request("stream").Add("id", Id(id)).Add("format", format);
            if (_configuration.MaximumBitRate > 0) request.Add("maxBitRate", _configuration.MaximumBitRate);
            return new Uri(request.Url);
        }

        public string CoverArtUrl(string id) { return new SubsonicService { Configuration = _configuration }.GetCoverArtForId(id); }
        public string AvatarUrl() { return Request("getAvatar").Add("username", _configuration.Username).Url; }

        public PlaylistItem CreatePlaylistItem(Song song)
        {
            var service = new SubsonicService { Configuration = _configuration };
            return new PlaylistItem
            {
                SongId = song.Id, AlbumId = song.AlbumId, ArtistId = song.ArtistId, UserRating = song.UserRating,
                AccountKey = AccountKey, Title = song.Title, Artist = string.IsNullOrWhiteSpace(song.DisplayArtist) ? song.Artist : song.DisplayArtist, Duration = song.Duration,
                ReplayGain = song.ReplayGain,
                CoverArtId = song.CoverArt, CoverArtUrl = service.GetCoverArtForId(song.CoverArt),
                Uri = service.GetUriForFileWithId(song.Id), IsStarred = !string.IsNullOrEmpty(song.Starred), Type = PlaylistItemTypeEnum.Audio
            };
        }
    }
}
