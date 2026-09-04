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
                    navigationService.NavigateToViewModel<AlbumViewModel>(id);
                    break;
                case SubsonicModelTypeEnum.MusicDirectory:
                    navigationService.NavigateToViewModel<MusicDirectoryViewModel>(id);
                    break;
                case SubsonicModelTypeEnum.Artist:
                    navigationService.NavigateToViewModel<ArtistViewModel>(id);
                    break;
                case SubsonicModelTypeEnum.Folder:
                    navigationService.NavigateToViewModel<IndexViewModel>(id);
                    break;
                case SubsonicModelTypeEnum.Genre:
                    navigationService.NavigateToViewModel<GenreSongsViewModel>(id);
                    break;
                case SubsonicModelTypeEnum.LibraryAlbums:
                    navigationService.NavigateToViewModel<IndexViewModel>(id);
                    break;
                case SubsonicModelTypeEnum.LibraryArtists:
                    navigationService.NavigateToViewModel<ArtistsViewModel>();
                    break;
                case SubsonicModelTypeEnum.LibraryGenres:
                    navigationService.NavigateToViewModel<GenresViewModel>();
                    break;
                case SubsonicModelTypeEnum.LibraryRandomSongs:
                    navigationService.NavigateToViewModel<RandomSongsViewModel>();
                    break;
                case SubsonicModelTypeEnum.LibraryPlaylists:
                    navigationService.NavigateToViewModel<ManagePlaylistsViewModel>();
                    break;
                case SubsonicModelTypeEnum.LibraryFavourites:
                    navigationService.NavigateToViewModel<FavouritesViewModel>();
                    break;
                case SubsonicModelTypeEnum.LibraryRadio:
                    navigationService.NavigateToViewModel<RadioStationsViewModel>();
                    break;
                case SubsonicModelTypeEnum.LibraryNowPlaying:
                    navigationService.NavigateToViewModel<NowPlayingViewModel>();
                    break;
            }
        }

        #endregion
    }
}
