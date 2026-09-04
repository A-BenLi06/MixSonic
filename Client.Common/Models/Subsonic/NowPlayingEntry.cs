namespace Client.Common.Models.Subsonic
{
    using System;
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "entry", Namespace = "http://subsonic.org/restapi")]
    public class NowPlayingEntry : MusicDirectoryChild
    {
        [XmlAttribute("username")]
        public string Username { get; set; }

        [XmlAttribute("playerId")]
        public string PlayerId { get; set; }

        [XmlAttribute("playerName")]
        public string PlayerName { get; set; }

        [XmlAttribute("minutesAgo")]
        public int MinutesAgo { get; set; }

        public override Tuple<string, string> GetDescription()
        {
            var username = string.IsNullOrEmpty(Username) ? "someone" : Username;
            var player = string.IsNullOrEmpty(PlayerName) ? "Navidrome" : PlayerName;
            var time = MinutesAgo <= 0 ? "just now" : string.Format("{0} min ago", MinutesAgo);
            return new Tuple<string, string>(Title, string.Format("{0} on {1} - {2}", username, player, time));
        }
    }
}
