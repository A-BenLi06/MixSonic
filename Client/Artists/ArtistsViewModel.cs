namespace Mixdrome.Artists
{
    using System.Collections.Generic;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using Mixdrome.Framework.ViewModel;

    public class ArtistsViewModel : CollectionViewModelBase<object, IList<ExpandedArtist>>, IArtistsViewModel
    {
        #region Constructors and Destructors

        public ArtistsViewModel()
        {
            UpdateDisplayName = () => DisplayName = "Artists";
        }

        #endregion

        #region Methods

        protected override async void OnActivate()
        {
            base.OnActivate();
            await Populate();
        }

        protected override IEnumerable<IMediaModel> GetItemsToDisplay(IList<ExpandedArtist> result)
        {
            return result;
        }

        protected override IServiceResultBase<IList<ExpandedArtist>> GetResult(object parameter)
        {
            return SubsonicService.GetArtists();
        }

        #endregion
    }
}
