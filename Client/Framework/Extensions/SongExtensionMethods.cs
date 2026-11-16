namespace Mixdrome.Framework.Extensions
{
    using Client.Common.Models;
    using Client.Common.Services;
    using Client.Common.Models.Subsonic;

    public static class SongExtensionMethods
    {
        #region Public Methods and Operators

        public static PlaylistItem AsPlaylistItem(this ISongModel songModel, ISubsonicService subsonicService)
        {
            var playlistItem = new PlaylistItem
                                   {
                                       SongId = songModel.Id,
                                       CoverArtId = songModel.CoverArt,
                                       AccountKey = AccountIdentity.For(subsonicService.Configuration),
                                       IsStarred = songModel is MusicDirectoryChild &&
                                                   !string.IsNullOrEmpty(((MusicDirectoryChild)songModel).Starred),
                                       Artist = songModel.Artist,
                                       Title = songModel.Name,
                                       Duration = songModel.Duration,
                                       Uri =
                                           songModel.Type == SubsonicModelTypeEnum.Video
                                               ? subsonicService.GetUriForVideoWithId(songModel.Id)
                                               : subsonicService.GetUriForFileWithId(songModel.Id),
                                       CoverArtUrl = subsonicService.GetCoverArtForId(songModel.CoverArt),
                                       Type =
                                           songModel.Type == SubsonicModelTypeEnum.Video
                                               ? PlaylistItemTypeEnum.Video
                                               : PlaylistItemTypeEnum.Audio
                                   };

            return playlistItem;
        }

        #endregion
    }
}
