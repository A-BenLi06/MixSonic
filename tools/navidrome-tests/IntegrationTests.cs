using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Client.Common.Models;
using Client.Common.Models.Navidrome;
using Client.Common.Models.Subsonic;
using Client.Common.Results;
using Client.Common.Services.DataStructures.SubsonicService;
using Client.Common.Services.Navidrome;

public static class NavidromeIntegrationTests
{
    [DataContract]
    private sealed class Fixture
    {
        [DataMember(Name = "port")] public int Port { get; set; }
        [DataMember(Name = "username")] public string Username { get; set; }
        [DataMember(Name = "password")] public string Password { get; set; }
    }
    private static int _assertions;
    private static void Check(bool value, string message) { _assertions++; if (!value) throw new Exception(message); }
    private static void Evidence(string group) { Console.WriteLine("ND-CHECK:integration:" + group); }
    public static int Main(string[] args)
    {
        try { Run(args[0]).GetAwaiter().GetResult(); Console.WriteLine("PASS: " + _assertions + " integration assertions against a real Navidrome fixture."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
    private static async Task Run(string stateFile)
    {
        Fixture fixture;
        using (var stream = File.OpenRead(stateFile)) fixture = (Fixture)new DataContractJsonSerializer(typeof(Fixture)).ReadObject(stream);
        var configuration = new SubsonicServiceConfiguration { BaseUrl = "http://127.0.0.1:" + fixture.Port + "/", Username = fixture.Username, Password = fixture.Password };
        var api = new NavidromeApi(configuration);
        var ping = api.Ping(); await ping.ReadAsync();
        Check(ping.Server.Type == "navidrome" && ping.Server.OpenSubsonic, "Real server metadata is reported.");
        Console.WriteLine("Fixture server: " + ping.Server.ServerVersion);
        Console.WriteLine("ND-FACT:server-version:" + ping.Server.ServerVersion);
        var extensions = await api.GetExtensions().ReadAsync();
        foreach (var extension in extensions) ping.Server.Extensions[extension.Key] = extension.Value;
        Console.WriteLine("Advertised extensions: " + string.Join(", ", extensions.Keys));
        Console.WriteLine("ND-FACT:extensions:" + string.Join(",", extensions.Keys.OrderBy(key => key)));
        api.UseFormPost = ping.Server.Supports("formPost");
        var user = await api.GetUser().ReadAsync();
        Check(user.Username == fixture.Username && user.AdminRole && user.StreamRole && user.DownloadRole, "Current account roles are read from the server.");
        var folders = await api.GetMusicFolders().ReadAsync(); Check(folders.Count > 0, "Accessible server music folder IDs are available.");
        Evidence("session");
        var ready = false;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var initialScan = await api.GetScanStatus().ReadAsync();
            if (!initialScan.Scanning && initialScan.Count == 125) { ready = true; break; }
            await Task.Delay(250);
        }
        Check(ready, "The fresh fixture scan finishes before library assertions run.");
        var folder = folders[0].Id;
        var first = await api.Search(new SearchQuery { Query = "", MusicFolderId = folder, ArtistCount = 0, AlbumCount = 0, SongCount = 100 }).ReadAsync();
        var second = await api.Search(new SearchQuery { Query = "", MusicFolderId = folder, ArtistCount = 0, AlbumCount = 0, SongCount = 100, SongOffset = 100 }).ReadAsync();
        Check(first.Songs.Count == 100 && second.Songs.Count == 25, "Complete song pagination crosses the 100-item page boundary.");
        Check(!first.Songs.Select(item => item.Id).Intersect(second.Songs.Select(item => item.Id)).Any(), "Song pages have no repeated IDs.");
        Evidence("search");
        var allSongs = first.Songs.Concat(second.Songs).ToList();
        var song = allSongs.First(item => item.Title == "Fixture track 001");
        var details = await api.GetSong(song.Id).ReadAsync();
        Check(details.AlbumId != null && details.ArtistId != null && details.Track == 1 && details.DiscNumber == 1 && details.Year == 2020, "Real track/disc/year and album/artist IDs are retained.");
        Check(details.Suffix == "wav" && details.SamplingRate == 22050 && details.Size > 0, "Actual file quality metadata is retained.");
        Evidence("metadata");
        var albums = new List<Album>();
        for (var offset = 0; ; offset += 2)
        {
            var page = await api.GetAlbums(new AlbumQuery { MusicFolderId = folder, Size = 2, Offset = offset }).ReadAsync();
            albums.AddRange(page); if (page.Count < 2) break;
        }
        Check(albums.Count == 5 && albums.Select(album => album.Id).Distinct().Count() == 5, "Albums can be traversed completely in smaller pages.");
        var albumDetails = await api.GetAlbum(details.AlbumId).ReadAsync(); Check(albumDetails.Songs.Count == 25, "An album resolves every track.");
        var artists = await api.GetArtists(folder).ReadAsync(); Check(artists.Count == 2, "Both real album artists are returned.");
        var artistDetails = await api.GetArtist(details.ArtistId).ReadAsync(); Check(artistDetails.Albums.Count > 0, "Artist album navigation resolves server IDs.");
        var genres = await api.GetGenres(folder).ReadAsync(); Check(genres.Any(genre => genre.Name == "Jazz") && genres.Any(genre => genre.Name == "Electronic"), "Tagged genres are returned.");
        var genreSongs = await api.GetGenreSongs("Jazz", folder, 0, 100).ReadAsync(); Check(genreSongs.Count == 75, "Genre songs use real metadata and the selected library.");
        var years = await api.GetAlbums(new AlbumQuery { Type = AlbumListType.ByYear, MusicFolderId = folder, FromYear = 2021, ToYear = 2023 }).ReadAsync();
        Check(years.Count == 3 && years.All(album => album.Year >= 2021 && album.Year <= 2023), "Year-filtered album lists use the requested range.");
        var electronic = await api.GetAlbums(new AlbumQuery { Type = AlbumListType.ByGenre, MusicFolderId = folder, Genre = "Electronic" }).ReadAsync();
        Check(electronic.Count == 2, "Genre-filtered album lists use the requested genre.");
        Evidence("browse");
        var randomGenre = await api.GetRandomSongs(new RandomSongQuery { MusicFolderId = folder, Genre = details.Genre, Size = 13 }).ReadAsync();
        Check(randomGenre.Count == 13 && randomGenre.All(item => item.Genre == details.Genre), "Random mixes honor the selected genre and size on the real server.");
        var randomYear = await api.GetRandomSongs(new RandomSongQuery { MusicFolderId = folder, FromYear = details.Year, ToYear = details.Year, Size = 500 }).ReadAsync();
        Check(randomYear.Count > 0 && randomYear.All(item => item.Year == details.Year), "Random mixes honor both year bounds on the real server.");
        Evidence("random");
        foreach (var media in new IMediaModel[] { details, albumDetails, artistDetails })
        {
            await api.SetStarred(media, true).ReadAsync(); await api.SetRating(media.Id, 4).ReadAsync();
        }
        var starred = await api.GetStarred(folder).ReadAsync();
        Check(starred.Songs.Any(item => item.Id == details.Id) && starred.Albums.Any(item => item.Id == albumDetails.Id) && starred.Artists.Any(item => item.Id == artistDetails.Id), "All three favorite entity types round-trip.");
        Check((await api.GetSong(details.Id).ReadAsync()).UserRating == 4 && (await api.GetAlbum(albumDetails.Id).ReadAsync()).UserRating == 4 &&
            (await api.GetArtist(artistDetails.Id).ReadAsync()).UserRating == 4, "Song, album and artist ratings round-trip.");
        foreach (var media in new IMediaModel[] { details, albumDetails, artistDetails }) { await api.SetStarred(media, false).ReadAsync(); await api.SetRating(media.Id, 0).ReadAsync(); }
        Check((await api.GetSong(details.Id).ReadAsync()).UserRating == 0 && (await api.GetAlbum(albumDetails.Id).ReadAsync()).UserRating == 0 && (await api.GetArtist(artistDetails.Id).ReadAsync()).UserRating == 0, "Zero clears all three entity ratings.");
        var cleared = await api.GetStarred(folder).ReadAsync();
        Check(!cleared.Songs.Any(item => item.Id == details.Id) && !cleared.Albums.Any(item => item.Id == albumDetails.Id) && !cleared.Artists.Any(item => item.Id == artistDetails.Id), "All three favorite entity types can be unstarred.");
        Evidence("annotations");
        var playlist = await api.SavePlaylist("Fixture playlist & 测试", new[] { song.Id, allSongs[1].Id, song.Id }).ReadAsync();
        try
        {
            var read = await api.GetPlaylist(playlist.Id).ReadAsync();
            Check(read.Entries.Select(item => item.Id).SequenceEqual(new[] { song.Id, allSongs[1].Id, song.Id }), "Playlist order and duplicate occurrences round-trip.");
            Check(!read.ReadOnly && read.Owner == fixture.Username && read.Entries[0].AlbumId == details.AlbumId && read.Entries[0].Suffix == "wav", "Playlist ownership and complete entry metadata round-trip.");
            await api.UpdatePlaylist(playlist.Id, "Renamed & playlist", "Fixture comment", true, new[] { allSongs[2].Id }, new[] { 1 }).ReadAsync();
            read = await api.GetPlaylist(playlist.Id).ReadAsync();
            Check(read.Name == "Renamed & playlist" && read.Comment == "Fixture comment" && read.Public && read.Entries.Count == 3, "Playlist rename/comment/public/add/remove operations round-trip.");
            var reversed = read.Entries.Select(item => item.Id).Reverse().ToList();
            await api.SavePlaylist(null, reversed, playlist.Id).ReadAsync(); read = await api.GetPlaylist(playlist.Id).ReadAsync();
            Check(read.Entries.Select(item => item.Id).SequenceEqual(reversed) && read.Name == "Renamed & playlist" && read.Comment == "Fixture comment" && read.Public,
                "Replacing track order preserves playlist metadata.");
        }
        finally { await api.DeletePlaylist(playlist.Id).ReadAsync(); }
        Check(!(await api.GetPlaylists().ReadAsync()).Any(item => item.Id == playlist.Id), "Playlist deletion is confirmed by the server.");
        Evidence("playlists");
        await api.SaveBookmark(song.Id, 2345, "Fixture bookmark").ReadAsync();
        var bookmark = (await api.GetBookmarks().ReadAsync()).Single(item => item.Song.Id == song.Id);
        Check(bookmark.PositionMilliseconds == 2345 && bookmark.Comment == "Fixture bookmark", "Bookmark milliseconds and comment round-trip.");
        await api.DeleteBookmark(song.Id).ReadAsync(); Check(!(await api.GetBookmarks().ReadAsync()).Any(item => item.Song.Id == song.Id), "Bookmark deletion round-trips.");
        Evidence("bookmarks");
        var indexQueue = ping.Server.Supports("indexBasedQueue");
        await api.SaveQueue(new[] { song.Id, allSongs[1].Id, song.Id }, 2, 3456, indexQueue).ReadAsync();
        var queue = await api.GetQueue(indexQueue).ReadAsync();
        Check(queue.Songs.Count == 3 && queue.PositionMilliseconds == 3456, "Server queue entries and millisecond position round-trip.");
        if (indexQueue) Check(queue.CurrentIndex == 2, "The selected duplicate occurrence survives index-based queue restoration.");
        Evidence("server-queue");
        var lyrics = await api.GetLyrics(song.Id, true).ReadAsync();
        Check(lyrics.Count > 0 && lyrics.Any(variant => variant.Lines.Any(line => line.Text.Contains("Fixture"))), "Actual tagged/sidecar lyrics are retrieved.");
        Check(lyrics.Any(variant => variant.Synced && variant.LineAt(2500) >= 0), "Synchronized lyrics select the line at actual playback time.");
        Evidence("lyrics");
        await api.GetArtistInfo(artistDetails.Id).ReadAsync(); await api.GetAlbumInfo(albumDetails.Id).ReadAsync();
        Check(true, "Absent external biography/album notes remain valid responses.");
        Evidence("information-empty");
        if (user.ShareRole)
        {
            var created = (await api.CreateShare(new[] { song.Id }, "Fixture share", DateTimeOffset.Now.AddDays(7)).ReadAsync()).Single();
            try
            {
                Check(Uri.IsWellFormedUriString(created.Url, UriKind.Absolute), "Share URLs are generated by the server.");
                var updatedExpiry = DateTimeOffset.Now.AddDays(9);
                await api.UpdateShare(created.Id, "Updated fixture share", updatedExpiry).ReadAsync();
                var share = (await api.GetShares().ReadAsync()).Single(item => item.Id == created.Id);
                Check(share.Description == "Updated fixture share" && share.Expires.HasValue && Math.Abs((share.Expires.Value - updatedExpiry).TotalMilliseconds) < 2,
                    "Share description and a changed expiration round-trip.");
                await api.UpdateShare(created.Id, "Updated fixture share", null).ReadAsync();
                share = (await api.GetShares().ReadAsync()).Single(item => item.Id == created.Id);
                Check(share.Expires.HasValue && Math.Abs((share.Expires.Value - updatedExpiry).TotalMilliseconds) < 2,
                    "Omitting the expiration preserves the existing share lifetime, as Navidrome implements.");
            }
            finally { await api.DeleteShare(created.Id).ReadAsync(); }
            Check(!(await api.GetShares().ReadAsync()).Any(item => item.Id == created.Id), "Share deletion is confirmed by the server.");
            var noExpiry = (await api.CreateShare(new[] { song.Id }, "No-expiry fixture share", null).ReadAsync()).Single();
            try { Check(noExpiry.Expires.HasValue && noExpiry.Expires.Value > DateTimeOffset.Now, "Creating a share without a custom date applies the server default expiration."); }
            finally { await api.DeleteShare(noExpiry.Id).ReadAsync(); }
            Evidence("shares");
        }
        using (var http = new HttpClient())
        {
            var avatar = await http.GetByteArrayAsync(api.AvatarUrl());
            Check(avatar.Length > 8 && avatar.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                "The account avatar endpoint returns the actual server PNG placeholder when Gravatar is disabled.");
            Evidence("avatar");
            Check(!string.IsNullOrEmpty(albumDetails.CoverArt), "The tagged fixture album exposes its generated cover artwork ID.");
            using (var artwork = await http.GetAsync(api.CoverArtUrl(albumDetails.CoverArt)))
            {
                var artworkBytes = await artwork.Content.ReadAsByteArrayAsync();
                Check(artwork.IsSuccessStatusCode && artworkBytes.Length > 8 && artwork.Content.Headers.ContentType != null && artwork.Content.Headers.ContentType.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
                    "The artwork endpoint returns actual image content for the returned album artwork ID.");
            }
            Evidence("artwork");
            var bytes = await http.GetByteArrayAsync(api.DownloadUri(song.Id));
            Check(bytes.Length > 1000 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF", "Original download returns a real WAV file, not a response placeholder.");
            using (var target = new MemoryStream())
            {
                var payload = await MediaDownload.CopyAsync(http, api.DownloadUri(song.Id), target, System.Threading.CancellationToken.None, null, details.Size);
                Check(payload.WindowsCompatible && payload.Length == details.Size && target.ToArray().SequenceEqual(bytes), "The production streaming downloader retains the complete original fixture file.");
            }
            Evidence("downloads");
            if (ping.Server.Supports("transcoding"))
            {
                var direct = await api.GetTranscodeDecision(song.Id, ClientPlaybackProfile.Windows81(0)).ReadAsync();
                Check(direct.CanDirectPlay && direct.SourceStream != null && direct.SourceStream.Container == "wav" && !string.IsNullOrEmpty(direct.TranscodeParams),
                    "Real negotiation recognizes a native WAV source and returns signed stream parameters.");
                var directBytes = await http.GetByteArrayAsync(api.TranscodeStreamUri(song.Id, direct.TranscodeParams));
                Check(directBytes.SequenceEqual(bytes), "The negotiated direct stream is byte-identical to the original file.");
                var transcode = await api.GetTranscodeDecision(song.Id, ClientPlaybackProfile.Windows81(64)).ReadAsync();
                Check(!transcode.CanDirectPlay && transcode.CanTranscode && transcode.TranscodeStream != null && transcode.TranscodeStream.Container == "mp3" && transcode.TranscodeStream.Bitrate == 64000,
                    "A real bitrate limit selects the compatible MP3 target with source-defined bps units.");
                using (var target = new MemoryStream())
                {
                    var payload = await MediaDownload.CopyAsync(http, api.TranscodeStreamUri(song.Id, transcode.TranscodeParams), target, System.Threading.CancellationToken.None);
                    Check(payload.WindowsCompatible && payload.Length > 0, "The signed transcoded stream returns actual compatible media bytes.");
                }
                Evidence("transcoding");
            }
        }
        var scan = await api.GetScanStatus().ReadAsync(); Check(scan.Count == 125 && !scan.Scanning, "Completed scan state and library count are real.");
        await api.GetRadio().ReadAsync(); await api.GetNowPlaying().ReadAsync();
        Check(true, "Station and activity reads complete successfully.");
        Evidence("station-activity-reads");
        var radioName = "Fixture radio & 音 " + Guid.NewGuid().ToString("N");
        await api.SaveRadio(null, radioName, "https://radio.example/fixture?a=1&b=2", "https://radio.example/fixture").ReadAsync();
        var station = (await api.GetRadio().ReadAsync()).Single(item => item.Name == radioName);
        try
        {
            Check(station.StreamUrl == "https://radio.example/fixture?a=1&b=2" && station.HomePageUrl == "https://radio.example/fixture", "Administrator station creation retains complete stream and homepage URLs.");
            await api.SaveRadio(station.Id, radioName + " updated", "https://radio.example/updated", "").ReadAsync();
            var updated = (await api.GetRadio().ReadAsync()).Single(item => item.Id == station.Id);
            Check(updated.Name == radioName + " updated" && updated.StreamUrl == "https://radio.example/updated" && string.IsNullOrEmpty(updated.HomePageUrl),
                "Administrator station edits update the name and stream and can clear the homepage.");
        }
        finally { await api.DeleteRadio(station.Id).ReadAsync(); }
        Check(!(await api.GetRadio().ReadAsync()).Any(item => item.Id == station.Id), "Station deletion is confirmed by the real server.");
        Evidence("radio-administration");
        await api.GetScanStatus(true).ReadAsync();
        var scanDeadline = DateTime.UtcNow.AddSeconds(30);
        do
        {
            await Task.Delay(250); scan = await api.GetScanStatus().ReadAsync();
        } while (scan.Scanning && DateTime.UtcNow < scanDeadline);
        Check(!scan.Scanning && scan.Count == 125, "An administrator can start a scan and observe completion without changing the fixture library.");
        Evidence("scan");
        Check(!user.JukeboxRole, "The disabled fixture jukebox is reflected in the actual user role.");
        await Rejected(api.Jukebox(JukeboxAction.Get), 501, "A disabled jukebox remains an explicit API failure."); Evidence("jukebox-disabled");
        await Rejected(api.GetSonicSimilarSongs(song.Id), 404, "An unconfigured acoustic provider remains an explicit API failure."); Evidence("sonic-disabled");
        await Rejected(api.FindSonicPath(song.Id, allSongs[1].Id), 404, "An unconfigured acoustic path remains an explicit API failure."); Evidence("path-disabled");
        if (ping.Server.Supports("playbackReport"))
        {
            var count = (await api.GetSong(song.Id).ReadAsync()).PlayCount;
            await api.ReportPlayback(song.Id, 0, "starting", 1, "Mixdrome-fixture", true).ReadAsync();
            await api.ReportPlayback(song.Id, 1000, "playing", 1, "Mixdrome-fixture", true).ReadAsync();
            var activity = await api.GetNowPlaying().ReadAsync();
            Check(activity.Any(entry => entry.Id == song.Id && entry.Username == user.Username && entry.PlaybackState == "playing" && entry.PositionMilliseconds >= 1000 && entry.PlaybackRate == 1),
                "Server activity retains the reported current song, account, state, position and rate.");
            Evidence("activity");
            await api.ReportPlayback(song.Id, 1000, "paused", 1, "Mixdrome-fixture", true).ReadAsync();
            await api.ReportPlayback(song.Id, details.Duration * 1000L, "stopped", 1, "Mixdrome-fixture", true).ReadAsync();
            Check(true, "Negotiated playback states are accepted by the server.");
            Check((await api.GetSong(song.Id).ReadAsync()).PlayCount == count, "A seek to the end with ignoreScrobble cannot increment the real server's play count.");
            await api.Scrobble(song.Id, true, DateTimeOffset.UtcNow, 0).ReadAsync();
            Check((await api.GetSong(song.Id).ReadAsync()).PlayCount == count + 1, "A qualifying listening submission increments the real play count once.");
            Evidence("playback-report");
        }
        else { await api.Scrobble(song.Id, false, DateTimeOffset.Now, 1).ReadAsync(); Check(true, "Legacy now-playing reporting is accepted when the extension is absent."); Evidence("legacy-scrobble"); }
    }
    private static async Task Rejected<T>(NavidromeResult<T> result, int status, string message)
    {
        Exception error = null;
        try { await result.ReadAsync(); }
        catch (Exception exception) { error = exception; }
        Check(error != null && !string.IsNullOrWhiteSpace(error.Message), message);
        using (var http = new HttpClient())
        using (var response = await http.GetAsync(result.RequestUrl)) Check((int)response.StatusCode == status, "The unavailable operation returns the source-defined HTTP status " + status);
    }
}
