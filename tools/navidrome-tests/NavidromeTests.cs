using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Client.Common.Models;
using Client.Common.Models.Navidrome;
using Client.Common.Models.Subsonic;
using Client.Common.Results;
using Client.Common.Services;
using Client.Common.Services.DataStructures.SubsonicService;
using Client.Common.Services.Navidrome;
using Common.Results;

public static class NavidromeTests
{
    private static int _assertions;
    private static readonly string Ns = "http://subsonic.org/restapi";
    private static SubsonicServiceConfiguration Configuration()
    { return new SubsonicServiceConfiguration { BaseUrl = "https://localhost/music/", Username = "review & 测试", Password = "secret&+?" }; }
    private static XDocument Xml(string body)
    { return XDocument.Parse("<subsonic-response xmlns='" + Ns + "' status='ok' type='navidrome' version='1.16.1' serverVersion='fixture' openSubsonic='true'>" + body + "</subsonic-response>"); }
    private static void Check(bool value, string name) { _assertions++; if (!value) throw new Exception(name); }
    private static void Throws<T>(Action action, string name) where T : Exception
    { try { action(); } catch (T) { _assertions++; return; } throw new Exception(name); }
    private static HttpRequestMessage Message<T>(NavidromeResult<T> result)
    { return (HttpRequestMessage)result.GetType().GetMethod("CreateRequest", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(result, null); }
    private static string Param(string query, string key)
    {
        return query.TrimStart('?').Split('&').Select(p => p.Split(new[] { '=' }, 2)).Where(p => p.Length == 2 && Uri.UnescapeDataString(p[0]) == key)
            .Select(p => Uri.UnescapeDataString(p[1].Replace("+", " "))).FirstOrDefault();
    }
    public static void Main()
    {
        try
        {
            Requests(); Metadata(); Extensions(); Pagination(); Lyrics(); Listening(); Offline(); Downloads().GetAwaiter().GetResult(); Queue(); Mutations(); Failures().GetAwaiter().GetResult();
            Console.WriteLine("PASS: " + _assertions + " Navidrome protocol/model assertions against production sources.");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }

    private static void Requests()
    {
        var configuration = Configuration();
        var request = new SubsonicRequest(configuration, "getSong").Add("id", "id&+= #测试");
        var uri = new Uri(request.Url);
        Check(uri.AbsolutePath == "/music/rest/getSong.view", "Reverse proxy path is preserved");
        Check(Param(uri.Query, "u") == configuration.Username, "Username is encoded");
        Check(Param(uri.Query, "p") == configuration.EncodedPassword, "Password parameter retains encoded value");
        Check(Param(uri.Query, "id") == "id&+= #测试", "Opaque Unicode ID is encoded");
        Check(Param(uri.Query, "c") == "Subsonic8", "Application identifies itself");
        var api = new NavidromeApi(configuration);
        var oldAccount = api.AccountKey;
        configuration.Username = "changed";
        Check(Param(new Uri(api.GetSong("s").RequestUrl).Query, "u") != "changed", "API captures request account");
        Check(api.AccountKey == oldAccount, "Snapshot account identity is stable");
        var service = new SubsonicService { Configuration = Configuration() };
        Check(Param(service.GetUriForFileWithId("id&1").Query, "id") == "id&1", "Stream uses encoded IDs");
        service.Configuration.StreamingFormat = "mp3";
        service.Configuration.MaximumBitRate = 192;
        Check(Param(service.GetUriForFileWithId("s").Query, "maxBitRate") == "192", "Configured quality affects stream");
        Check(Param(service.GetUriForFileWithId("s").Query, "format") == "mp3", "The chosen stream format reaches the protocol");
        service.Configuration.StreamingFormat = "raw";
        Check(Param(service.GetUriForFileWithId("s").Query, "format") == "raw", "Original streaming is an explicit server request");
        service.Configuration.StreamingFormat = null; service.Configuration.MaximumBitRate = 0;
        Check(Param(service.GetUriForFileWithId("s").Query, "format") == null && Param(service.GetUriForFileWithId("s").Query, "maxBitRate") == null, "Server defaults omit format and bitrate constraints");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Check(Param(new SubsonicRequest(Configuration(), "stream").Add("timeOffset", 1.25).Url.Split('?')[1], "timeOffset") == "1.25", "Protocol numbers are invariant");
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    private static void Metadata()
    {
        var api = new NavidromeApi(Configuration());
        var song = api.GetSong("s");
        song.HandleResponse(Xml("<song id='s' title='标题' artistId='ar' albumId='al' track='3' discNumber='2' year='2024' genre='Jazz' duration='230' bitRate='900' samplingRate='48000' bitDepth='24' channelCount='2' size='4294967296' starred='2026-11-09T00:00:00Z' userRating='4'><artists id='ar' name='A'/><artists id='b' name='B'/><genres name='Jazz'/><replayGain trackGain='-5.5' trackPeak='0.95'/></song>"));
        Check(song.Result.Title == "标题" && song.Result.Artists.Count == 2, "Multi-artist and Unicode metadata");
        Check(song.Result.AlbumId == "al" && song.Result.Track == 3 && song.Result.DiscNumber == 2, "Stable album/disc/track fields");
        Check(song.Result.Size == 4294967296L && song.Result.SamplingRate == 48000, "Large file and quality fields");
        Check(song.Result.UserRating == 4 && !string.IsNullOrEmpty(song.Result.Starred), "Annotations deserialize");
        Check(song.Result.ReplayGain.TrackGainSpecified && song.Result.ReplayGain.TrackGain == -5.5, "Optional replay gain present");
        Check(!song.Result.ReplayGain.AlbumGainSpecified, "Missing gain is distinct from zero");
        var artists = api.GetArtists(null);
        artists.HandleResponse(Xml("<artists><index name='A'><artist id='a' name='Artist' coverArt='ar-a' starred='2026-11-09T00:00:00Z' userRating='5'/></index></artists>"));
        Check(artists.Result[0].CoverArt == "ar-a" && artists.Result[0].UserRating == 5, "Artist artwork and annotations survive");
        var favorites = api.GetStarred(null);
        favorites.HandleResponse(Xml("<starred2><artist id='a' name='A'/><album id='al' name='AL'/><song id='s' title='S'/></starred2>"));
        Check(favorites.Result.Artists.Count == 1 && favorites.Result.Albums.Count == 1 && favorites.Result.Songs.Count == 1, "All favorite entity types");
        var playlist = api.GetPlaylist("p");
        playlist.HandleResponse(Xml("<playlist id='p' name='P' owner='owner' readonly='true' comment='notes' public='true' coverArt='pl-p' created='2026-11-09T00:00:00Z'><entry id='s' title='S'/><entry id='s' title='S'/></playlist>"));
        Check(playlist.Result.ReadOnly && playlist.Result.Comment == "notes" && playlist.Result.CoverArt == "pl-p", "Playlist permissions and metadata");
        Check(playlist.Result.Entries.Count == 2, "Playlist duplicates retained");
    }

    private static void Extensions()
    {
        var api = new NavidromeApi(Configuration());
        var result = api.GetExtensions();
        result.HandleResponse(Xml("<openSubsonicExtensions name='songLyrics'><versions>1</versions><versions>2</versions></openSubsonicExtensions><openSubsonicExtensions name='indexBasedQueue'><versions>1</versions></openSubsonicExtensions>"));
        Check(result.Result["songLyrics"].SequenceEqual(new[] { 1, 2 }), "Actual extension XML shape");
        Check(result.Server.Type == "navidrome" && result.Server.OpenSubsonic, "Reported server metadata");
        var capabilities = result.Server;
        foreach (var entry in result.Result) capabilities.Extensions[entry.Key] = entry.Value;
        Check(capabilities.Supports("songLyrics", 2) && !capabilities.Supports("unknown"), "Capabilities use exact advertised versions");
        var user = api.GetUser();
        user.HandleResponse(Xml("<user username='user' adminRole='false' downloadRole='true' shareRole='false' streamRole='true' scrobblingEnabled='true'/>"));
        Check(user.Result.DownloadRole && !user.Result.ShareRole && !user.Result.AdminRole, "Server role restrictions retained");
    }

    private static void Pagination()
    {
        var api = new NavidromeApi(Configuration());
        foreach (AlbumListType type in Enum.GetValues(typeof(AlbumListType)))
        {
            var query = new AlbumQuery { Type = type, Offset = 500, Size = 100, MusicFolderId = "lib&1", FromYear = 2024, ToYear = 1970, Genre = "R&B" };
            var uri = new Uri(api.GetAlbums(query).RequestUrl);
            Check(Param(uri.Query, "type") == query.ApiType && Param(uri.Query, "offset") == "500", "Album type/page " + type);
            Check(Param(uri.Query, "musicFolderId") == "lib&1", "Folder on album page " + type);
        }
        var search = api.Search(new SearchQuery { Query = "", ArtistCount = 0, AlbumCount = 0, SongCount = 100, SongOffset = 600, MusicFolderId = "2" });
        var parameters = new Uri(search.RequestUrl).Query;
        Check(Param(parameters, "songOffset") == "600" && Param(parameters, "artistCount") == "0" && Param(parameters, "query") == "", "Independent search/sync query");
        search.HandleResponse(Xml("<searchResult3><song id='s' title='S'/></searchResult3>"));
        Check(search.Result.Songs.Count == 1 && search.Result.Albums.Count == 0, "Paged results map");
        Throws<ArgumentOutOfRangeException>(() => api.GetAlbums(new AlbumQuery { Size = 501 }), "500 server maximum enforced");
    }

    private static void Lyrics()
    {
        var result = new NavidromeApi(Configuration()).GetLyrics("s", true);
        result.HandleResponse(Xml("<lyricsList><structuredLyrics lang='eng' kind='main' synced='true' offset='500' displayTitle='S'><line start='0'>one</line><line start='1000'>two</line></structuredLyrics><structuredLyrics lang='jpn' synced='false'><line>翻訳</line></structuredLyrics></lyricsList>"));
        Check(result.Result.Count == 2 && result.Result[1].Language == "jpn", "Returned lyric variants");
        var variant = result.Result[0];
        Check(variant.LineAt(499) == -1 && variant.LineAt(500) == 0 && variant.LineAt(1500) == 1, "Positive lyric offset and boundaries");
        variant.OffsetMilliseconds = -500;
        Check(variant.LineAt(500) == 1, "Negative lyric offset");
        Check(result.Result[1].LineAt(3000) == -1, "Unsynced lyric does not invent timing");
        variant.OffsetMilliseconds = 0;
        variant.Lines.Add(new LyricsLine { StartMilliseconds = 250, Text = "out of order" });
        Check(variant.LineAt(999) == 2 && variant.LineAt(1000) == 1, "Returned line order cannot select a later timestamp too soon");
        variant.Lines.Add(new LyricsLine { StartMilliseconds = 1000, Text = "parallel translation" });
        variant.Lines.Add(new LyricsLine { Text = "untimed annotation" });
        Check(variant.LineAt(1000) == 3, "Equal timestamps resolve consistently and untimed annotations do not become current");
        variant.OffsetMilliseconds = long.MinValue;
        Check(variant.LineAt(long.MaxValue) == 3, "Extreme server offsets cannot overflow the playback timeline");
        variant.OffsetMilliseconds = long.MaxValue;
        Check(variant.LineAt(long.MinValue) == -1, "Negative effective playback time does not wrap to a current line");
        variant.Lines.Clear();
        Check(variant.LineAt(1000) == -1, "An empty synchronized variant has no highlighted line");
        result.HandleResponse(Xml("<lyricsList/>"));
        Check(result.Result.Count == 0, "Missing lyrics is valid empty data");
    }

    private static void Queue()
    {
        var api = new NavidromeApi(Configuration());
        var result = api.GetQueue(true);
        result.HandleResponse(Xml("<playQueueByIndex currentIndex='2' position='123456' changedBy='other'><entry id='s' title='A'/><entry id='b' title='B'/><entry id='s' title='A'/></playQueueByIndex>"));
        Check(result.Result.CurrentIndex == 2 && result.Result.Songs[0].Id == result.Result.Songs[2].Id, "Duplicate occurrence by index");
        Check(result.Result.PositionMilliseconds == 123456, "Milliseconds retained without narrowing");
        using (var message = Message(api.SaveQueue(Enumerable.Range(0, 5001).Select(i => "s&" + i), 5000, 123456, true)))
        {
            var body = message.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Check(message.Method == HttpMethod.Post && message.RequestUri.Query == "", "Long queue uses form POST");
            Check(body.Split('&').Count(p => p.StartsWith("id=")) == 5001, "No long-queue truncation");
            Check(Param(body, "currentIndex") == "5000" && Param(body, "position") == "123456", "Index and position POST values");
        }
        result.HandleResponse(Xml("<playQueueByIndex/>"));
        Check(result.Result.Songs.Count == 0 && result.Result.CurrentIndex == -1, "Empty queue has no invented selected song");
        Throws<InvalidOperationException>(() => result.HandleResponse(Xml("<playQueueByIndex currentIndex='2'><entry id='s'/></playQueueByIndex>")), "Invalid queue selection rejected");
        Throws<ArgumentOutOfRangeException>(() => api.SaveQueue(new[] { "s" }, 1, 0, true), "Invalid queue save rejected");
        var bookmarks = api.GetBookmarks();
        bookmarks.HandleResponse(Xml("<bookmarks><bookmark position='123456' comment='resume'><entry id='s' title='S'/></bookmark></bookmarks>"));
        Check(bookmarks.Result[0].PositionMilliseconds == 123456 && bookmarks.Result[0].Song.Id == "s", "Bookmark uses returned song/milliseconds");
    }

    private static void Listening()
    {
        var counter = new ListeningCounter { DurationSeconds = 120 };
        counter.Observe(0, true, 1); counter.Observe(20, false, 1);
        Check(counter.ListenedSeconds == 20 && !counter.Qualified, "Playing integrates real elapsed time");
        counter.Observe(200, false, 1);
        Check(counter.ListenedSeconds == 20, "Pause cannot add a listen");
        counter.Observe(200, true, 1); counter.Observe(205, true, 2); counter.Observe(220, false, 1);
        Check(counter.ListenedSeconds == 55 && !counter.Qualified, "Rate changes integrate the preceding rate");
        counter.Observe(300, true, 1); counter.Observe(305, false, 1);
        Check(counter.ListenedSeconds == 60 && counter.Qualified, "Half-duration qualifies at the exact boundary");
        var longTrack = new ListeningCounter { DurationSeconds = 900 };
        longTrack.Observe(0, true, 1); longTrack.Observe(239, true, 1);
        Check(!longTrack.Qualified, "Long tracks require four minutes");
        longTrack.Observe(240, false, 1); Check(longTrack.Qualified, "Four-minute threshold qualifies");
        var failedOpen = new ListeningCounter { DurationSeconds = 120 };
        failedOpen.Observe(0, false, 1); failedOpen.Observe(1000, false, 1);
        Check(failedOpen.ListenedSeconds == 0 && !failedOpen.Qualified, "Failed opens never qualify");
        var unknown = new ListeningCounter(); unknown.Observe(0, true, 1); unknown.Observe(1000, false, 1);
        Check(!unknown.Qualified, "Unknown duration cannot qualify");
        unknown.DurationSeconds = double.NaN; Check(!unknown.Qualified, "Invalid duration cannot qualify");
        unknown.DurationSeconds = double.PositiveInfinity; Check(!unknown.Qualified, "Infinite duration cannot qualify");
        Throws<ArgumentOutOfRangeException>(() => counter.Observe(304, true, 1), "Backward monotonic time rejected");
        Throws<ArgumentOutOfRangeException>(() => counter.Observe(double.NaN, true, 1), "NaN time rejected");
        Throws<ArgumentOutOfRangeException>(() => counter.Observe(306, true, 0), "Invalid rate rejected");
        var pending = new PendingListening { AccountKey = "account" };
        pending.Entries.Add(new PendingListen { OccurrenceId = "first", SongId = "曲 & one", StartedUtc = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc) });
        pending.Entries.Add(new PendingListen { OccurrenceId = "second", SongId = "曲 & one", StartedUtc = pending.Entries[0].StartedUtc.AddMinutes(4) });
        var xml = LocalXml.Serialize(pending);
        Check(xml.Contains("encoding=\"utf-8\""), "Catalog declaration matches FileIO UTF-8 bytes");
        var restored = LocalXml.Deserialize<PendingListening>(xml); restored.Validate("account");
        Check(restored.Entries.Count == 2 && restored.Entries[0].SongId == "曲 & one" && restored.Entries[0].StartedUtc.Kind == DateTimeKind.Utc,
            "Repeated listens retain occurrence IDs, Unicode and UTC timestamps");
        Throws<InvalidDataException>(() => restored.Validate("other account"), "Cross-account pending records rejected");
        restored.Entries[1].OccurrenceId = restored.Entries[0].OccurrenceId;
        Throws<InvalidDataException>(() => restored.Validate("account"), "Duplicate pending occurrence rejected");
        var lyric = new LyricVariant { Synced = true, OffsetMilliseconds = -200, Language = "eng" };
        lyric.Lines.Add(new LyricsLine { StartMilliseconds = 1000, Text = "line & 音" });
        var lyrics = LocalXml.Deserialize<List<LyricVariant>>(LocalXml.Serialize(new List<LyricVariant> { lyric }));
        Check(lyrics[0].LineAt(800) == 0 && lyrics[0].Lines[0].Text == "line & 音", "Offline lyric XML retains variants and timing");
        using (var message = Message(new NavidromeApi(Configuration()).ReportPlayback("s", 119000, "stopped", 1, "device", true)))
        {
            var body = message.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Check(Param(body, "ignoreScrobble") == "true" && Param(body, "positionMs") == "119000", "Presence reports suppress position-based false scrobbles");
        }
    }

    private static void Mutations()
    {
        var api = new NavidromeApi(Configuration());
        foreach (var item in new SubsonicModelBase[] { new Song { Id = "s" }, new Album { Id = "a" }, new ExpandedArtist { Id = "ar" } })
        {
            using (var message = Message(api.SetStarred(item, true)))
            {
                var body = message.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var key = item is ExpandedArtist ? "artistId" : item is Album ? "albumId" : "id";
                Check(Param(body, key) == item.Id, "Correct favorite ID field " + key);
            }
        }
        Throws<ArgumentOutOfRangeException>(() => api.SetRating("s", 6), "Invalid rating rejected");
        using (var message = Message(api.SetRating("s", 0))) Check(Param(message.Content.ReadAsStringAsync().GetAwaiter().GetResult(), "rating") == "0", "Zero clears rating");
        using (var message = Message(api.UpdatePlaylist("p", null, "", null, null, new[] { 2, 0 })))
        {
            var body = message.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Check(Param(body, "name") == null && Param(body, "public") == null && Param(body, "comment") == "", "Unrequested playlist fields omitted and blank comment allowed");
            Check(body.Split('&').Count(p => p.StartsWith("songIndexToRemove=")) == 2, "Repeated playlist removal indices");
        }
        var shares = api.GetShares();
        shares.HandleResponse(Xml("<shares><share id='share' url='https://localhost/share/token' description='D' expires='2026-12-01T00:00:00Z' visitCount='2'/></shares>"));
        Check(shares.Result[0].Expires.HasValue && shares.Result[0].Url.EndsWith("token"), "Share URL/expiry from response");
        shares.HandleResponse(Xml("<shares><share id='share' expires='1970-01-01T00:00:00Z'/></shares>"));
        Check(!shares.Result[0].Expires.HasValue, "The Unix epoch expiration sentinel is treated as no expiration");
        Check(NavidromeApi.UnixMilliseconds(new DateTimeOffset(1970,1,1,0,0,1,TimeSpan.Zero)) == 1000, "Unix timestamps are milliseconds");
    }

    private static async Task Failures()
    {
        var result = new NavidromeApi(Configuration()).SetRating("s", 5);
        var succeeded = false;
        result.OnSuccess(value => succeeded = true);
        result.GetResourceFunc = () => Task.FromResult(new HttpStreamResult { Stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<subsonic-response xmlns='" + Ns + "' status='failed'><error code='50' message='not allowed'/></subsonic-response>")) });
        await result.Execute();
        Check(result.Error != null && !succeeded, "Failed server mutation never confirms success");
        var lyrics = new NavidromeApi(Configuration()).GetLyrics("s", false);
        lyrics.GetResourceFunc = () => Task.FromResult(new HttpStreamResult { Exception = new IOException("fixture disconnect") });
        try { await lyrics.ReadAsync(); throw new Exception("Failure must propagate"); }
        catch (IOException) { Check(lyrics.Result == null, "Disconnected read does not invent an empty successful result"); }
    }

    private static void Offline()
    {
        var name = new string('a', 32) + "-original.flac";
        var catalog = new OfflineCatalog { AccountKey = "account" };
        catalog.Songs.Add(new DownloadedSong { Song = new Song { Id = "s", Title = "曲 & one", Artists = new List<MediaArtist> { new MediaArtist { Id = "ar", Name = "A" } } },
            OriginalFile = name, OriginalSize = 5000, CompletedUtc = DateTime.UtcNow });
        catalog.Validate("account");
        var restored = LocalXml.Deserialize<OfflineCatalog>(LocalXml.Serialize(catalog)); restored.Validate("account");
        Check(restored.Songs[0].Song.Artists[0].Id == "ar" && restored.Songs[0].Song.Title == "曲 & one", "Offline catalogs preserve full metadata and Unicode");
        Throws<InvalidDataException>(() => restored.Validate("other"), "Offline catalog cannot cross accounts");
        foreach (var invalid in new[] { "../outside.mp3", @"C:\outside.mp3", @"..\outside.mp3", "catalog.xml", name + ".partial", "https://host/track", "/root/file" })
            Check(!OfflineCatalog.IsFileName(invalid), "Unsafe/pending catalog filename rejected: " + invalid);
        Check(OfflineCatalog.Suffix("FLAC") == "flac" && OfflineCatalog.Suffix("../mp3") == "bin", "Server suffix cannot become a path");
        var export = OfflineCatalog.ExportName(new Song { Title = "CON" });
        Check(export == "_CON" && OfflineCatalog.ExportName(new Song { Title = "a/b:c" }) == "a_b_c", "Export names respect Windows path and device-name rules");
        restored.Songs.Add(restored.Songs[0]); Throws<InvalidDataException>(() => restored.Validate("account"), "Duplicate offline song records rejected");
        var localArt = "ms-appdata:///local/music/cover.jpg";
        Check(new PlaylistItem { CoverArtUrl = localArt }.OriginalCoverArtUrl == localArt, "Offline artwork never receives a server size query");
        var api = new NavidromeApi(Configuration());
        Check(Param(api.StreamUri("id&one", "mp3").Query, "format") == "mp3" && Param(api.StreamUri("id&one", "mp3").Query, "id") == "id&one",
            "Compatible offline stream requests retain format and opaque IDs");
    }

    private sealed class ChunkedStream : MemoryStream
    {
        public ChunkedStream(byte[] bytes) : base(bytes) { }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellation)
        { return base.ReadAsync(buffer, offset, Math.Min(count, 37), cancellation); }
    }
    private sealed class MediaHandler : HttpMessageHandler
    {
        public byte[] Bytes;
        public string Type = "application/octet-stream";
        public long? DeclaredLength;
        public System.Net.HttpStatusCode Status = System.Net.HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(Status) { Content = new StreamContent(new ChunkedStream(Bytes)) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(Type);
            response.Content.Headers.ContentLength = DeclaredLength ?? Bytes.Length;
            return Task.FromResult(response);
        }
    }
    private sealed class DirectProgress : IProgress<DownloadProgress>
    {
        public DownloadProgress Last;
        public Action<DownloadProgress> Callback;
        public void Report(DownloadProgress value) { Last = value; if (Callback != null) Callback(value); }
    }
    private static async Task Downloads()
    {
        var wave = new byte[12000];
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, wave, 0, 4);
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "), 0, wave, 8, 8);
        Array.Copy(BitConverter.GetBytes((uint)16), 0, wave, 16, 4); wave[20] = 1;
        using (var http = new HttpClient(new MediaHandler { Bytes = wave, Type = "audio/wav" }))
        using (var target = new MemoryStream())
        {
            var progress = new DirectProgress();
            var payload = await MediaDownload.CopyAsync(http, new Uri("https://fixture/media"), target, System.Threading.CancellationToken.None, progress, wave.Length);
            Check(target.ToArray().SequenceEqual(wave) && payload.Length == wave.Length && payload.WindowsCompatible, "Chunked original PCM WAV downloads preserve actual bytes");
            Check(progress.Last.Received == wave.Length && progress.Last.Total == wave.Length, "Download progress reports actual received and total bytes");
        }
        foreach (var body in new[] { "<subsonic-response status='failed'/>", "  {\"error\":\"denied\"}", "\uFEFF[\"error\"]", "<!DOCTYPE html><title>sign in</title>" })
        using (var http = new HttpClient(new MediaHandler { Bytes = System.Text.Encoding.UTF8.GetBytes(body) }))
        using (var target = new MemoryStream())
        {
            var failed = false;
            try { await MediaDownload.CopyAsync(http, new Uri("https://fixture/media"), target, System.Threading.CancellationToken.None); }
            catch (InvalidDataException) { failed = true; }
            Check(failed && target.Length == 0, "HTTP 200 error documents cannot become media: " + body.Substring(0, 8));
        }
        var unicodeError = System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes("<error/>")).ToArray();
        foreach (var handler in new[] { new MediaHandler { Bytes = unicodeError }, new MediaHandler { Bytes = wave, Type = "application/xml" },
            new MediaHandler { Bytes = wave, DeclaredLength = wave.Length + 1 }, new MediaHandler { Bytes = new byte[0] } })
        using (var http = new HttpClient(handler))
        using (var target = new MemoryStream())
        {
            var failed = false; try { await MediaDownload.CopyAsync(http, new Uri("https://fixture/media"), target, System.Threading.CancellationToken.None); }
            catch (InvalidDataException) { failed = true; }
            Check(failed, "UTF-16 errors, document MIME, incomplete or empty downloads are rejected");
        }
        using (var http = new HttpClient(new MediaHandler { Bytes = wave }))
        using (var target = new MemoryStream())
        {
            var failed = false; try { await MediaDownload.CopyAsync(http, new Uri("https://fixture/media"), target, System.Threading.CancellationToken.None, null, wave.Length + 1); }
            catch (InvalidDataException) { failed = true; }
            Check(failed, "Original length is checked against server song metadata");
        }
        foreach (var bytes in new[] { wave, System.Text.Encoding.ASCII.GetBytes("fLaCfixture"), new byte[] { 255, 241, 1, 2 } })
        using (var http = new HttpClient(new MediaHandler { Bytes = bytes }))
        using (var target = new MemoryStream())
        {
            var failed = false; try { await MediaDownload.CopyAsync(http, new Uri("https://fixture/media"), target, System.Threading.CancellationToken.None, null, 0, true); }
            catch (InvalidDataException) { failed = true; }
            Check(failed && target.Length == 0, "Ignored MP3 format requests cannot create a falsely compatible copy");
        }
        using (var http = new HttpClient(new MediaHandler { Bytes = new byte[] { 255, 251, 144, 100, 1, 2 }, Type = "audio/mpeg" }))
        using (var target = new MemoryStream())
            Check((await MediaDownload.CopyAsync(http, new Uri("https://fixture/media"), target, System.Threading.CancellationToken.None, null, 0, true)).WindowsCompatible,
                "MP3 frame headers are recognized for the requested compatible copy");
        using (var cancellation = new System.Threading.CancellationTokenSource())
        using (var http = new HttpClient(new MediaHandler { Bytes = wave }))
        using (var target = new MemoryStream())
        {
            var progress = new DirectProgress { Callback = value => cancellation.Cancel() };
            var cancelled = false; try { await MediaDownload.CopyAsync(http, new Uri("https://fixture/media"), target, cancellation.Token, progress); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && target.Length < wave.Length, "Cancellation interrupts streaming before completion");
        }
    }
}
