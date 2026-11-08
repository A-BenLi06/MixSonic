namespace Client.Common.EventAggregatorMessages
{
    using Client.Common.Models;

    public class TrackPlaybackStartedMessage : PlaybackMessageBase
    {
        public TrackPlaybackStartedMessage(PlaylistItem item) : base(item)
        {
        }
    }
}
