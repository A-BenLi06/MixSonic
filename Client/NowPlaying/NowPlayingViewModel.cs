namespace Mixdrome.NowPlaying
{
    using System.Collections.Generic;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using Mixdrome.Framework.ViewModel;

    public class NowPlayingViewModel : CollectionViewModelBase<object, IList<NowPlayingEntry>>, INowPlayingViewModel
    {
        public NowPlayingViewModel()
        {
            UpdateDisplayName = () => DisplayName = "now playing";
        }

        public override string EmptyStateSubtitle
        {
            get
            {
                return "When another client starts playing from this server, the track, user, and player will show up here.";
            }
        }

        public override string EmptyStateTitle
        {
            get
            {
                return "Nobody is playing right now";
            }
        }

        protected override async void OnActivate()
        {
            base.OnActivate();
            await Populate();
        }

        protected override IEnumerable<IMediaModel> GetItemsToDisplay(IList<NowPlayingEntry> result)
        {
            return result;
        }

        protected override IServiceResultBase<IList<NowPlayingEntry>> GetResult(object parameter)
        {
            return SubsonicService.GetNowPlaying();
        }
    }
}
