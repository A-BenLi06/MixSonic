namespace Mixdrome.Genres
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using Mixdrome.Framework.ViewModel;

    public class GenreSongsViewModel : CollectionViewModelBase<string, IList<Song>>, IGenreSongsViewModel
    {
        #region Constructors and Destructors

        public GenreSongsViewModel()
        {
            UpdateDisplayName = () => DisplayName = string.IsNullOrEmpty(Parameter) ? "Genre" : Parameter;
        }

        #endregion

        #region Methods

        protected override Task AfterPopulate(string parameter)
        {
            UpdateDisplayName();
            return base.AfterPopulate(parameter);
        }

        protected override IEnumerable<IMediaModel> GetItemsToDisplay(IList<Song> result)
        {
            return result;
        }

        protected override IServiceResultBase<IList<Song>> GetResult(string parameter)
        {
            return SubsonicService.GetSongsByGenre(parameter);
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
