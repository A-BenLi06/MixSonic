namespace Mixdrome.Framework
{
    using Mixdrome.Framework.Services;

    public interface ITileNotificationCapable
    {
        #region Public Properties

        ITileNotificationService TileNotificationService { get; }

        #endregion
    }
}