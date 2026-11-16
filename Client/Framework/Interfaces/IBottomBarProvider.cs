namespace Mixdrome.Framework.Interfaces
{
    using Mixdrome.BottomBar;

    public interface IBottomBarViewModelProvider
    {
        #region Public Properties

        IBottomBarViewModel BottomBar { get; set; }

        #endregion
    }
}