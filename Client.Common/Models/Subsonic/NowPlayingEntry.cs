namespace Client.Common.Models.Subsonic
{
    using System;
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "entry", Namespace = "http://subsonic.org/restapi")]
    public class NowPlayingEntry : Song
    {
        [XmlAttribute("username")]
        public string Username { get; set; }

        [XmlAttribute("playerId")]
        public string PlayerId { get; set; }

        [XmlAttribute("playerName")]
        public string PlayerName { get; set; }

        [XmlAttribute("minutesAgo")]
        public int MinutesAgo { get; set; }

        [XmlAttribute("state")] public string PlaybackState { get; set; }
        [XmlAttribute("positionMs")] public long PositionMilliseconds { get; set; }
        [XmlAttribute("playbackRate")] public double PlaybackRate { get; set; }

        public override Tuple<string, string> GetDescription()
        {
            return new Tuple<string, string>(Title, Artist);
        }
    }
}
