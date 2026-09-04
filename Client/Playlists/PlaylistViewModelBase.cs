namespace Subsonic8.Playlists
{
    using System.Collections.Generic;
    using System.Linq;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using Subsonic8.BottomBar;
    using Subsonic8.Framework.ViewModel;

    public abstract class PlaylistViewModelBase : CollectionViewModelBase<object, PlaylistCollection>,
                                                  IPlaylistViewModel
    {
        #region Public Properties

        public override string EmptyStateSubtitle
        {
            get
            {
                return "Save the current queue as a remote playlist, then load it from any Navidrome client.";
            }
        }

        public override string EmptyStateTitle
        {
            get
            {
                return "No remote playlists";
            }
        }

        #endregion

        #region Methods

        protected override IEnumerable<IMediaModel> GetItemsToDisplay(PlaylistCollection result)
        {
            return result.Playlists.Select(x => new GenericMediaModel(x));
        }

        protected override IServiceResultBase<PlaylistCollection> GetResult(object parameter)
        {
            return SubsonicService.GetAllPlaylists();
        }

        protected override void LoadBottomBar()
        {
            BottomBar = BottomBar ?? GetPlaylistBottomBar();
        }

        protected override async void OnActivate()
        {
            base.OnActivate();
            await Populate();
        }

        protected override void OnViewLoaded(object view)
        {
            MenuItems.Clear();
            base.OnViewLoaded(view);
        }

        private IPlaylistBottomBarViewModel GetPlaylistBottomBar()
        {
            var playlistBottomBarViewModel = IoCService.Get<IPlaylistBottomBarViewModel>();
            playlistBottomBarViewModel.OnPlaylistDeleted = async () => await Populate();

            return playlistBottomBarViewModel;
        }

        #endregion
    }
}
