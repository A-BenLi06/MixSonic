namespace Mixdrome.Artist
{
    using System.Collections.Generic;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using Mixdrome.Framework.ViewModel;
    using Mixdrome.MenuItem;

    public class ArtistViewModel : DetailViewModelBase<ExpandedArtist>, IArtistViewModel
    {
        #region Constructors and Destructors

        public ArtistViewModel()
        {
            MenuItems = new BindableCollection<MenuItemViewModel>();
        }

        #endregion

        #region Methods

        protected override IEnumerable<IMediaModel> GetItemsToDisplay(ExpandedArtist result)
        {
            return result.Albums;
        }

        protected override IServiceResultBase<ExpandedArtist> GetResult(string id)
        {
            return SubsonicService.GetArtist(id);
        }

        #endregion
    }
}