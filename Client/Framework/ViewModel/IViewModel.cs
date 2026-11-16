namespace Mixdrome.Framework.ViewModel
{
    using Caliburn.Micro;
    using Client.Common.Services;
    using Mixdrome.ErrorDialog;
    using Mixdrome.Framework.Services;
    using Action = System.Action;

    public interface IViewModel : IScreen, ISongLoader
    {
        #region Public Properties

        bool CanGoBack { get; }

        IEventAggregator EventAggregator { get; set; }

        ICustomFrameAdapter NavigationService { get; set; }

        IDialogNotificationService NotificationService { get; set; }

        Action UpdateDisplayName { get; set; }

        IErrorDialogViewModel ErrorDialogViewModel { get; set; }

        #endregion

        #region Public Methods and Operators

        void GoBack();

        #endregion
    }
}