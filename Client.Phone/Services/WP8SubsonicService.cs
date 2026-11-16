namespace Mixdrome.Phone.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;

    /// <summary>
    /// WP8.1-specific Subsonic service implementation using WinRT HttpClient.
    /// </summary>
    public class WP8SubsonicService : IDisposable
    {
        private readonly WinRTHttpClient _httpClient;
        private string _baseUrl;
        private string _username;
        private string _password;
        private bool _compatibleMode;

        public WP8SubsonicService()
        {
            _httpClient = new WinRTHttpClient();
        }

        public void Configure(string baseUrl, string username, string password)
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _username = username ?? string.Empty;
            _password = password ?? string.Empty;
        }

        /// <summary>
        /// Sets the compatible mode (transcode lossless to MP3 320kbps).
        /// </summary>
        public void SetCompatibleMode(bool enabled)
        {
            _compatibleMode = enabled;
        }

        public bool CompatibleMode
        {
            get { return _compatibleMode; }
            set { _compatibleMode = value; }
        }

        private string BuildApiUrl(string method, Dictionary<string, string> parameters = null)
        {
            var url = $"{_baseUrl}/rest/{method}?u={Uri.EscapeDataString(_username)}&p={Uri.EscapeDataString(_password)}&v=1.16.1&c=MixRadioWP&f=xml";
            
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    url += $"&{param.Key}={Uri.EscapeDataString(param.Value)}";
                }
            }
            
            return url;
        }

        public async Task<bool> PingAsync()
        {
            try
            {
                var url = BuildApiUrl("ping");
                var responseXml = await _httpClient.GetStringAsync(url);
                var doc = XDocument.Parse(responseXml);
                var root = doc.Root;
                
                if (root == null) return false;
                
                var ns = root.Name.Namespace;
                var status = root.Attribute("status")?.Value;
                return status == "ok";
            }
            catch
            {
                return false;
            }
        }

        public async Task<Song> GetSongAsync(string id)
        {
            var url = BuildApiUrl("getSong", new Dictionary<string, string> { { "id", id } });
            var responseXml = await _httpClient.GetStringAsync(url);
            var doc = XDocument.Parse(responseXml);
            var root = doc.Root;
            
            if (root == null) return null;
            
            var ns = root.Name.Namespace;
            var songElement = root.Element(ns + "song");
            
            if (songElement == null) return null;
            
            return new Song
            {
                Id = songElement.Attribute("id")?.Value,
                Title = songElement.Attribute("title")?.Value,
                Artist = songElement.Attribute("artist")?.Value,
                Album = songElement.Attribute("album")?.Value,
                CoverArt = songElement.Attribute("coverArt")?.Value,
                Duration = ParseInt(songElement.Attribute("duration")?.Value)
            };
        }

        public Uri GetStreamUri(string songId)
        {
            var parameters = new Dictionary<string, string> { { "id", songId } };
            
            // When compatible mode is enabled, transcode to MP3 320kbps
            if (_compatibleMode)
            {
                parameters.Add("format", "mp3");
                parameters.Add("maxBitRate", "320");
            }
            
            var url = BuildApiUrl("stream", parameters);
            return new Uri(url);
        }

        public string GetCoverArtUrl(string coverArtId, int size = 300)
        {
            return BuildApiUrl("getCoverArt", new Dictionary<string, string>
            {
                { "id", coverArtId },
                { "size", size.ToString() }
            });
        }

        public async Task<List<InternetRadioStation>> GetInternetRadioStationsAsync()
        {
            var stations = new List<InternetRadioStation>();
            var url = BuildApiUrl("getInternetRadioStations");

            try
            {
                var responseXml = await _httpClient.GetStringAsync(url);
                var doc = XDocument.Parse(responseXml);
                var root = doc.Root;

                if (root == null) return stations;

                var ns = root.Name.Namespace;
                var stationsElement = root.Element(ns + "internetRadioStations");

                if (stationsElement == null) return stations;

                foreach (var stationElement in stationsElement.Elements(ns + "internetRadioStation"))
                {
                    stations.Add(new InternetRadioStation
                    {
                        Id = stationElement.Attribute("id")?.Value,
                        Name = stationElement.Attribute("name")?.Value,
                        StreamUrl = stationElement.Attribute("streamUrl")?.Value,
                        HomePageUrl = stationElement.Attribute("homePageUrl")?.Value
                    });
                }
            }
            catch
            {
            }

            return stations;
        }

        public async Task<List<Song>> GetStarredSongsAsync()
        {
            var songs = new List<Song>();
            var url = BuildApiUrl("getStarred2");

            try
            {
                var responseXml = await _httpClient.GetStringAsync(url);
                var doc = XDocument.Parse(responseXml);
                var root = doc.Root;

                if (root == null) return songs;

                var ns = root.Name.Namespace;
                var starredElement = root.Element(ns + "starred2");

                if (starredElement == null) return songs;

                foreach (var songElement in starredElement.Elements(ns + "song"))
                {
                    songs.Add(ParseSong(songElement));
                }
            }
            catch
            {
            }

            return songs;
        }

        public async Task<List<NowPlayingEntry>> GetNowPlayingAsync()
        {
            var entries = new List<NowPlayingEntry>();
            var url = BuildApiUrl("getNowPlaying");

            try
            {
                var responseXml = await _httpClient.GetStringAsync(url);
                var doc = XDocument.Parse(responseXml);
                var root = doc.Root;

                if (root == null) return entries;

                var ns = root.Name.Namespace;
                var nowPlayingElement = root.Element(ns + "nowPlaying");

                if (nowPlayingElement == null) return entries;

                foreach (var entryElement in nowPlayingElement.Elements(ns + "entry"))
                {
                    entries.Add(new NowPlayingEntry
                    {
                        Song = ParseSong(entryElement),
                        Username = entryElement.Attribute("username")?.Value,
                        PlayerId = entryElement.Attribute("playerId")?.Value,
                        PlayerName = entryElement.Attribute("playerName")?.Value,
                        MinutesAgo = ParseInt(entryElement.Attribute("minutesAgo")?.Value)
                    });
                }
            }
            catch
            {
            }

            return entries;
        }

        public async Task<List<Album>> GetAlbumsAsync(string artistId)
        {
            var albums = new List<Album>();
            var url = BuildApiUrl("getArtist", new Dictionary<string, string> { { "id", artistId } });
            
            try
            {
                var responseXml = await _httpClient.GetStringAsync(url);
                var doc = XDocument.Parse(responseXml);
                var root = doc.Root;
                
                if (root == null) return albums;
                
                var ns = root.Name.Namespace;
                var artistElement = root.Element(ns + "artist");
                
                if (artistElement == null) return albums;
                
                foreach (var albumElement in artistElement.Elements(ns + "album"))
                {
                    albums.Add(new Album
                    {
                        Id = albumElement.Attribute("id")?.Value,
                        Name = albumElement.Attribute("name")?.Value,
                        Artist = albumElement.Attribute("artist")?.Value,
                        CoverArt = albumElement.Attribute("coverArt")?.Value
                    });
                }
            }
            catch
            {
                // Return empty list on error
            }
            
            return albums;
        }

        public async Task<List<Song>> GetAlbumSongsAsync(string albumId)
        {
            var songs = new List<Song>();
            var url = BuildApiUrl("getAlbum", new Dictionary<string, string> { { "id", albumId } });
            
            try
            {
                var responseXml = await _httpClient.GetStringAsync(url);
                var doc = XDocument.Parse(responseXml);
                var root = doc.Root;
                
                if (root == null) return songs;
                
                var ns = root.Name.Namespace;
                var albumElement = root.Element(ns + "album");
                
                if (albumElement == null) return songs;
                
                foreach (var songElement in albumElement.Elements(ns + "song"))
                {
                    songs.Add(ParseSong(songElement));
                }
            }
            catch
            {
                // Return empty list on error
            }
            
            return songs;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        private static int ParseInt(string value)
        {
            int result;
            return int.TryParse(value, out result) ? result : 0;
        }

        private static Song ParseSong(XElement songElement)
        {
            return new Song
            {
                Id = songElement.Attribute("id")?.Value,
                Title = songElement.Attribute("title")?.Value,
                Artist = songElement.Attribute("artist")?.Value,
                Album = songElement.Attribute("album")?.Value,
                CoverArt = songElement.Attribute("coverArt")?.Value,
                Duration = ParseInt(songElement.Attribute("duration")?.Value)
            };
        }
    }

    public class InternetRadioStation
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string StreamUrl { get; set; }
        public string HomePageUrl { get; set; }
    }

    public class NowPlayingEntry
    {
        public Song Song { get; set; }
        public string Username { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int MinutesAgo { get; set; }
    }
}
