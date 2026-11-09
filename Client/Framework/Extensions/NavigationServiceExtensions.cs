namespace Subsonic8.Framework.Extensions
{
    using Client.Common.Models;
    using Client.Common.Services;
    using Subsonic8.Album;
    using Subsonic8.Artist;
    using Subsonic8.Artists;
    using Subsonic8.Favourites;
    using Subsonic8.Genres;
    using Subsonic8.Index;
    using Subsonic8.MusicDirectory;
    using Subsonic8.NowPlaying;
    using Subsonic8.Playback;
    using Subsonic8.Playlists;
    using Subsonic8.Radio;
    using Subsonic8.RandomSongs;
    using Subsonic8.Navidrome;

    public static class NavigationServiceExtensions
    {
        #region Public Methods and Operators

        public static void NavigateByModelType<T>(this T navigationService, ISubsonicModel subsonicModel)
            where T : ICustomFrameAdapter
        {
            var id = subsonicModel.Id;
            switch (subsonicModel.Type)
            {
                case SubsonicModelTypeEnum.Song:
                case SubsonicModelTypeEnum.Video:
                    navigationService.NavigateToViewModel<PlaybackViewModel>();
                    break;
                case SubsonicModelTypeEnum.Album:
                    navigationService.NavigateToViewModel<NavidromeDetailViewModel>(NavidromeRoute.For("album", id));
                    break;
                case SubsonicModelTypeEnum.MusicDirectory:
                    navigationService.NavigateToViewModel<MusicDirectoryViewModel>(id);
                    break;
                case SubsonicModelTypeEnum.Artist:
                    navigationService.NavigateToViewModel<NavidromeDetailViewModel>(NavidromeRoute.For("artist", id));
                    break;
                case SubsonicModelTypeEnum.Folder:
                    navigationService.NavigateToViewModel<IndexViewModel>(id);
                    break;
                case SubsonicModelTypeEnum.Genre:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>(NavidromeRoute.For("genre", subsonicModel.Name));
                    break;
                case SubsonicModelTypeEnum.LibraryAlbums:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>("albums");
                    break;
                case SubsonicModelTypeEnum.LibraryArtists:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>("artists");
                    break;
                case SubsonicModelTypeEnum.LibraryGenres:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>("genres");
                    break;
                case SubsonicModelTypeEnum.LibraryRandomSongs:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>("random");
                    break;
                case SubsonicModelTypeEnum.LibraryPlaylists:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>("playlists");
                    break;
                case SubsonicModelTypeEnum.LibraryFavourites:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>("favourites");
                    break;
                case SubsonicModelTypeEnum.LibraryRadio:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>("radio");
                    break;
                case SubsonicModelTypeEnum.LibraryNowPlaying:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>("activity");
                    break;
                case SubsonicModelTypeEnum.LibrarySongs:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>("songs");
                    break;
                case SubsonicModelTypeEnum.LibraryBookmarks:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>("bookmarks");
                    break;
                case SubsonicModelTypeEnum.LibraryShares:
                    navigationService.NavigateToViewModel<NavidromeBrowserViewModel>("shares");
                    break;
                case SubsonicModelTypeEnum.LibraryDownloads:
                    navigationService.NavigateToViewModel<NavidromeDownloadsViewModel>();
                    break;
                case SubsonicModelTypeEnum.Playlist:
                    navigationService.NavigateToViewModel<NavidromeDetailViewModel>(NavidromeRoute.For("playlist", id));
                    break;
            }
        }

        #endregion
    }
}
