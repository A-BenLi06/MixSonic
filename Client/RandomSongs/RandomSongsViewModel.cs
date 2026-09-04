namespace Subsonic8.RandomSongs
{
    using System.Collections.Generic;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using Subsonic8.Framework.ViewModel;

    public class RandomSongsViewModel : CollectionViewModelBase<object, IList<Song>>, IRandomSongsViewModel
    {
        #region Constants

        private const int SongsToLoad = 50;

        #endregion

        #region Constructors and Destructors

        public RandomSongsViewModel()
        {
            UpdateDisplayName = () => DisplayName = "Random Mix";
        }

        #endregion

        #region Methods

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
            return SubsonicService.GetRandomSongs(SongsToLoad);
        }

                public override bool IsGroupedView
        {
            get
            {
                return false;
            }
        }

#endregion
    }
}
