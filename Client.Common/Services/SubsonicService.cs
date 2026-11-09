namespace Client.Common.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text.RegularExpressions;
    using Caliburn.Micro;
    using Client.Common.Results;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Client.Common.Services.Navidrome;

    public class SubsonicService : PropertyChangedBase, ISubsonicService
    {
        #region Constants

        public const string CoverArtPlaceholder = @"/Assets/CoverArtPlaceholder.jpg";

        #endregion

        #region Fields

        private SubsonicServiceConfiguration _configuration;

        #endregion

        #region Constructors and Destructors

        public SubsonicService()
        {
            GetMusicFolders = GetMusicFoldersImpl;
            GetMusicDirectory = GetMusicDirectoryImpl;
            GetAlbum = GetAlbumImpl;
            GetArtist = GetArtistImpl;
            GetSong = GetSongImpl;
            GetIndex = GetIndexImpl;
            Search = SearchImpl;
            GetAllPlaylists = GetAllPlaylistsImpl;
            GetArtists = GetArtistsImpl;
            GetGenres = GetGenresImpl;
            GetPlaylist = GetPlaylistImpl;
            DeletePlaylist = DeletePlaylistImpl;
            CreatePlaylist = CreatePlaylistImpl;
            UpdatePlaylist = UpdatePlaylistResultImpl;
            RenamePlaylist = RenamePlaylistImpl;
            Ping = PingImpl;
            GetRandomSongs = GetRandomSongsImpl;
            GetSongsByGenre = GetSongsByGenreImpl;
            GetAlbumList = GetAlbumListImpl;
            GetStarredSongs = GetStarredSongsImpl;
            SetStarred = (id, starred) => new SetStarredResult(Configuration, id, starred);
            GetInternetRadioStations = GetInternetRadioStationsImpl;
            GetNowPlaying = GetNowPlayingImpl;
        }

        #endregion

        #region Public Properties

        public SubsonicServiceConfiguration Configuration
        {
            get
            {
                return _configuration;
            }

            set
            {
                _configuration = value;
                NotifyOfPropertyChange();
            }
        }

        public Func<string, IEnumerable<string>, ICreatePlaylistResult> CreatePlaylist { get; set; }

        public Func<string, IDeletePlaylistResult> DeletePlaylist { get; set; }

        public Func<string, IGetAlbumResult> GetAlbum { get; set; }

        public Func<IGetAllPlaylistsResult> GetAllPlaylists { get; set; }

        public Func<IGetAllArtistsResult> GetArtists { get; set; }

        public Func<string, IGetArtistResult> GetArtist { get; set; }

        public Func<IGetGenresResult> GetGenres { get; set; }

        public Func<string, IGetIndexResult> GetIndex { get; set; }

        public Func<string, IGetMusicDirectoryResult> GetMusicDirectory { get; set; }

        public Func<IGetRootResult> GetMusicFolders { get; set; }

        public Func<string, IGetPlaylistResult> GetPlaylist { get; set; }

        public Func<string, IGetSongResult> GetSong { get; set; }

        public virtual bool HasValidSubsonicUrl
        {
            get
            {
                return Configuration != null && !string.IsNullOrEmpty(Configuration.BaseUrl);
            }
        }

        public Func<IPingResult> Ping { get; set; }

        public Func<string, string, IRenamePlaylistResult> RenamePlaylist { get; set; }

        public Func<string, ISearchResult> Search { get; set; }

        public Func<string, IEnumerable<string>, IEnumerable<int>, IUpdatePlaylistResult> UpdatePlaylist { get; set; }

        public Func<int, IGetRandomSongsResult> GetRandomSongs { get; set; }

        public Func<string, IGetSongsByGenreResult> GetSongsByGenre { get; set; }

        public Func<string, IGetAlbumListResult> GetAlbumList { get; set; }

        public Func<IGetStarredSongsResult> GetStarredSongs { get; set; }

        public Func<string, bool, ISetStarredResult> SetStarred { get; set; }

        public Func<IGetInternetRadioStationsResult> GetInternetRadioStations { get; set; }

        public Func<IGetNowPlayingResult> GetNowPlaying { get; set; }

        public bool IsVideoPlaybackInitialized { get; set; }

        #endregion

        #region Public Methods and Operators

        public string GetCoverArtForId(string coverArt)
        {
            return GetCoverArtForId(coverArt, ImageType.Thumbnail);
        }

        public virtual string GetCoverArtForId(string coverArt, ImageType imageType)
        {
            string result;
            if (!string.IsNullOrEmpty(coverArt))
            {
                result = new SubsonicRequest(_configuration, "getCoverArt")
                    .Add("id", coverArt).Add("size", (int)imageType).Url;
            }
            else
            {
                result = CoverArtPlaceholder;
            }

            return result;
        }

        public virtual Uri GetUriForFileWithId(string id)
        {
            if (_configuration.CompatibleMode) return GetUriForFileWithId(id, true);
            var request = new SubsonicRequest(_configuration, "stream").Add("id", id);
            if (!string.IsNullOrEmpty(_configuration.StreamingFormat)) request.Add("format", _configuration.StreamingFormat);
            if (_configuration.MaximumBitRate > 0) request.Add("maxBitRate", _configuration.MaximumBitRate);
            return new Uri(request.Url);
        }

        public virtual Uri GetUriForFileWithId(string id, bool transcodeToMp3)
        {
            var request = new SubsonicRequest(_configuration, "stream").Add("id", id);
            if (transcodeToMp3) request.Add("format", "mp3").Add("maxBitRate", _configuration.MaximumBitRate > 0 ? _configuration.MaximumBitRate : 320);
            return new Uri(request.Url);
        }

        public Uri GetUriForVideoStartingAt(Uri source, double totalSeconds)
        {
            var uriString = source.ToString();
            var regex = new Regex("(.*)(?<TIMEOFFSET>&timeOffset=)([0-9]{1,})(.*)");

            var regeExFormat = string.Format("$1${{TIMEOFFSET}}{0}$3", Math.Floor(totalSeconds));

            return new Uri(regex.Replace(uriString, regeExFormat));
        }

        public virtual Uri GetUriForVideoWithId(string id, int timeOffset = 0, int maxBitRate = 0)
        {
            var uriString = string.Format(
                "{0}stream/stream.ts?id={1}&hls=true&timeOffset={2}", _configuration.BaseUrl, id, timeOffset);
            if (maxBitRate > 0)
            {
                uriString += string.Format("&maxBitRate={0}", maxBitRate);
            }

            return new Uri(uriString);
        }

        #endregion

        #region Methods

        private ICreatePlaylistResult CreatePlaylistImpl(string name, IEnumerable<string> songIds)
        {
            return new CreatePlaylistResult(Configuration, name, songIds);
        }

        private IDeletePlaylistResult DeletePlaylistImpl(string id)
        {
            return new DeletePlaylistResult(Configuration, id);
        }

        private IGetAlbumResult GetAlbumImpl(string id)
        {
            return new GetAlbumResult(_configuration, id);
        }

        private IGetAllPlaylistsResult GetAllPlaylistsImpl()
        {
            return new GetAllPlaylistsResult(_configuration);
        }

        private IGetAllArtistsResult GetArtistsImpl()
        {
            return new GetAllArtistsResult(_configuration);
        }

        private IGetArtistResult GetArtistImpl(string id)
        {
            return new GetArtistsResult(_configuration, id);
        }

        private IGetGenresResult GetGenresImpl()
        {
            return new GetGenresResult(_configuration);
        }

        private IGetIndexResult GetIndexImpl(string musicFolderId)
        {
            return new GetIndexResult(_configuration, musicFolderId);
        }

        private IGetMusicDirectoryResult GetMusicDirectoryImpl(string id)
        {
            return new GetMusicDirectoryResult(_configuration, id);
        }

        private IGetRootResult GetMusicFoldersImpl()
        {
            return new GetRootResult(_configuration);
        }

        private IGetPlaylistResult GetPlaylistImpl(string id)
        {
            return new GetPlaylistResult(_configuration, id);
        }

        private IGetSongResult GetSongImpl(string id)
        {
            return new GetSongResult(_configuration, id);
        }

        private PingResult PingImpl()
        {
            return new PingResult(Configuration);
        }

        private IRenamePlaylistResult RenamePlaylistImpl(string id, string name)
        {
            return new RenamePlaylistResult(Configuration, id, name);
        }

        private ISearchResult SearchImpl(string query)
        {
            return new SearchResult(_configuration, query);
        }

        private IUpdatePlaylistResult UpdatePlaylistResultImpl(
            string id, IEnumerable<string> songIdsToAdd, IEnumerable<int> songIndexesToRemove)
        {
            return new UpdatePlaylistResult(Configuration, id, songIdsToAdd, songIndexesToRemove);
        }

        private IGetRandomSongsResult GetRandomSongsImpl(int numberOfSongs)
        {
            return new GetRandomSongsResult(Configuration, numberOfSongs);
        }

        private IGetSongsByGenreResult GetSongsByGenreImpl(string genre)
        {
            return new GetSongsByGenreResult(Configuration, genre);
        }

        private IGetAlbumListResult GetAlbumListImpl(string musicFolderId)
        {
            return new GetAlbumListResult(Configuration, musicFolderId);
        }

        private IGetStarredSongsResult GetStarredSongsImpl()
        {
            return new GetStarredSongsResult(Configuration);
        }

        private IGetInternetRadioStationsResult GetInternetRadioStationsImpl()
        {
            return new GetInternetRadioStationsResult(Configuration);
        }

        private IGetNowPlayingResult GetNowPlayingImpl()
        {
            return new GetNowPlayingResult(Configuration);
        }

        #endregion
    }
}
