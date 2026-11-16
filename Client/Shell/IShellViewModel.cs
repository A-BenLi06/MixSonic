namespace Mixdrome.Shell
{
    using Caliburn.Micro;
    using Client.Common.Services;
    using Mixdrome.BottomBar;
    using Mixdrome.Framework.Interfaces;
    using Mixdrome.Framework.Services;
    using global::Common.Interfaces;

    public interface IShellViewModel : IViewAware,
                                       IScreen,
                                       IBottomBarViewModelProvider,
                                       IErrorHandler,
                                       IHandle<ChangeBottomBarMessage>
    {
        #region Public Properties

        IDialogNotificationService DialogNotificationService { get; set; }

        IToastNotificationService NotificationService { get; set; }

        ISubsonicService SubsonicService { get; set; }

        #endregion

        #region Public Methods and Operators

        void SendSearchQueryMessage(string query);

        #endregion
    }
}