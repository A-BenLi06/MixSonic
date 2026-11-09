namespace Client.Common.EventAggregatorMessages
{
    using System.Collections.Generic;
    using Client.Common.Models;

    public class PlayQueueMessage
    {
        public IList<PlaylistItem> Queue { get; set; }

        public int StartIndex { get; set; }

        public PlaybackStartOptions Options { get; set; }
    }
}
