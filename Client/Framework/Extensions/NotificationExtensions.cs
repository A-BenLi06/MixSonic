namespace Mixdrome.Framework.Extensions
{
    using System.Threading.Tasks;
    using Client.Common.Models;
    using Mixdrome.Framework.Interfaces;
    using Mixdrome.Framework.Services;

    internal static class NotificationExtensions
    {
        #region Public Methods and Operators

        public static async Task ShowToast<T>(this T notificationServiceOwner, PlaylistItem model)
            where T : IToastNotificationCapable
        {
            await notificationServiceOwner.ToastNotificationService.Show(GetNotificationOptionsFromPlaylistItem(model));
        }

        public static async Task UpdateTile<T>(this T notificationServiceOwner, PlaylistItem model)
            where T : ITileNotificationCapable
        {
            await notificationServiceOwner.TileNotificationService.Show(GetNotificationOptionsFromPlaylistItem(model));
        }

        #endregion

        #region Methods

        private static PlaybackNotificationOptions GetNotificationOptionsFromPlaylistItem(PlaylistItem model)
        {
            return new PlaybackNotificationOptions
                       {
                           ImageUrl = model.OriginalCoverArtUrl, 
                           Title = model.Title, 
                           Subtitle = model.Artist
                       };
        }

        #endregion
    }
}