namespace Mixdrome.Framework.Interfaces
{
    using Mixdrome.Framework.Services;

    public interface IToastNotificationCapable
    {
        #region Public Properties

        IToastNotificationService ToastNotificationService { get; }

        #endregion
    }
}