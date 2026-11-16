namespace Mixdrome.Listening
{
    using Mixdrome.Framework.ViewModel;

    public class RecentTrackViewModel : ItemViewModelBase
    {
        public RecentTrackViewModel(RecentTrack track)
        {
            Track = track;
            Title = track.Title;
            CoverArtId = track.CoverArtId;
        }

        public RecentTrack Track { get; private set; }
        public string Artist { get { return Track.Artist; } }
    }
}
