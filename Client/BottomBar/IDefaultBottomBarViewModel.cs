namespace Mixdrome.BottomBar
{
    using System;
    using System.Threading.Tasks;
    using Mixdrome.Framework.Services;
    using Mixdrome.Framework.ViewModel;

    public interface IDefaultBottomBarViewModel : IBottomBarViewModel, ISongLoader
    {
        #region Public Properties

        bool CanAddToPlaylist { get; }

        Action NavigateOnPlay { get; set; }

        IDialogNotificationService NotificationService { get; set; }

        #endregion

        #region Public Methods and Operators

        Task AddToPlaylist();

        Task PlayAll();

        #endregion
    }
}