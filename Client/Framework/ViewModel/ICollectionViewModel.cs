namespace Mixdrome.Framework.ViewModel
{
    using System;
    using System.Collections.ObjectModel;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Mixdrome.BottomBar;
    using Mixdrome.MenuItem;

    public interface ICollectionViewModel<TParameter> : IViewModel
    {
        #region Public Properties

        IBottomBarViewModel BottomBar { get; set; }

        string EmptyStateSubtitle { get; }

        string EmptyStateTitle { get; }

        bool HasItems { get; }

        bool IsEmpty { get; }

        BindableCollection<MenuItemViewModel> MenuItems { get; set; }

        TParameter Parameter { get; set; }

        ObservableCollection<object> SelectedItems { get; }

        Func<IId, Task<PlaylistItem>> LoadPlaylistItem { get; set; }

        #endregion

        #region Public Methods and Operators

        Task HandleItemSelection(ISubsonicModel subsonicModel);

        #endregion
    }
}
