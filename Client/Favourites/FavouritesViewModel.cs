namespace Subsonic8.Favourites
{
    using System.Collections.Generic;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using Subsonic8.Framework.ViewModel;

    public class FavouritesViewModel : CollectionViewModelBase<object, IList<Song>>, IFavouritesViewModel
    {
        public FavouritesViewModel()
        {
            UpdateDisplayName = () => DisplayName = MixRadioText.Get("Favourites");
        }

        public override string EmptyStateSubtitle
        {
            get
            {
                return MixRadioText.Get("FavouritesEmptySubtitle");
            }
        }

        public override string EmptyStateTitle
        {
            get
            {
                return MixRadioText.Get("FavouritesEmptyTitle");
            }
        }

        protected override async void OnActivate()
        {
            base.OnActivate();
            await Populate();
        }

        protected override IEnumerable<IMediaModel> GetItemsToDisplay(IList<Song> result)
        {
            return result;
        }

        protected override IServiceResultBase<IList<Song>> GetResult(object parameter)
        {
            return SubsonicService.GetStarredSongs();
        }
        public override bool IsGroupedView        {            get            {                return false;            }        }
    }
}
