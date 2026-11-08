namespace Subsonic8.Listening
{
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    public class RecentTrack
    {
        [DataMember] public string SongId { get; set; }
        [DataMember] public string CoverArtId { get; set; }
        [DataMember] public string Title { get; set; }
        [DataMember] public string Artist { get; set; }
        [DataMember] public int Duration { get; set; }
        [DataMember] public DateTimeOffset PlayedAt { get; set; }
    }
}
