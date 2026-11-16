namespace Mixdrome.Genres
{
    using System.Collections.Generic;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using Mixdrome.Framework.ViewModel;

    public class GenresViewModel : CollectionViewModelBase<object, IList<Genre>>, IGenresViewModel
    {
        #region Constructors and Destructors

        public GenresViewModel()
        {
            UpdateDisplayName = () => DisplayName = "Genres";
        }

        #endregion

        #region Methods

        protected override async void OnActivate()
        {
            base.OnActivate();
            await Populate();
        }

        protected override IEnumerable<IMediaModel> GetItemsToDisplay(IList<Genre> result)
        {
            return result;
        }

        protected override IServiceResultBase<IList<Genre>> GetResult(object parameter)
        {
            return SubsonicService.GetGenres();
        }

        #endregion
    }
}
