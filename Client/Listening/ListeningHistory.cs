namespace Mixdrome.Listening
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.Serialization;

    [DataContract]
    public class ListeningHistory
    {
        public const int MaximumEntries = 100;

        public ListeningHistory()
        {
            Entries = new List<RecentTrack>();
        }

        [DataMember]
        public List<RecentTrack> Entries { get; set; }

        public void Record(RecentTrack track)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.SongId)) return;
            Entries = new[] { track }.Concat((Entries ?? new List<RecentTrack>())
                .Where(entry => entry != null && entry.SongId != track.SongId)).Take(MaximumEntries).ToList();
        }
    }
}
