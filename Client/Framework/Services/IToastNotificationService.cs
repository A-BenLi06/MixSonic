namespace Mixdrome.Framework.Services
{
    public interface IToastNotificationService : INotificationService<PlaybackNotificationOptions>
    {
        #region Public Properties

        bool EnableNotifications { get; set; }

        #endregion
    }
}