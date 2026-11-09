namespace Client.Common.Models.Subsonic
{
    using System;
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "child", Namespace = "http://subsonic.org/restapi")]
    public class MusicDirectoryChild : MediaModelBase
    {
        #region Public Properties

        [XmlAttribute("album")]
        public string Album { get; set; }

        [XmlAttribute("artist")]
        public string Artist { get; set; }

        [XmlAttribute("albumId")] public string AlbumId { get; set; }
        [XmlAttribute("track")] public int Track { get; set; }
        [XmlAttribute("discNumber")] public int DiscNumber { get; set; }
        [XmlAttribute("year")] public int Year { get; set; }
        [XmlAttribute("genre")] public string Genre { get; set; }
        [XmlAttribute("suffix")] public string Suffix { get; set; }
        [XmlAttribute("contentType")] public string ContentType { get; set; }
        [XmlAttribute("bitRate")] public int BitRate { get; set; }
        [XmlAttribute("samplingRate")] public int SamplingRate { get; set; }
        [XmlAttribute("bitDepth")] public int BitDepth { get; set; }
        [XmlAttribute("channelCount")] public int ChannelCount { get; set; }
        [XmlAttribute("size")] public long Size { get; set; }
        [XmlAttribute("path")] public string Path { get; set; }
        [XmlAttribute("musicBrainzId")] public string MusicBrainzId { get; set; }
        [XmlElement("artists")] public System.Collections.Generic.List<MediaArtist> Artists { get; set; }
        [XmlElement("albumArtists")] public System.Collections.Generic.List<MediaArtist> AlbumArtists { get; set; }
        [XmlElement("genres")] public System.Collections.Generic.List<MediaGenre> Genres { get; set; }
        [XmlElement("replayGain")] public ReplayGain ReplayGain { get; set; }

        [XmlAttribute("duration")]
        public int Duration { get; set; }

        [XmlAttribute("isDir")]
        public bool IsDirectory { get; set; }

        [XmlAttribute("isVideo")]
        public bool IsVideo { get; set; }

        [XmlAttribute("name")]
        public override string Name
        {
            get
            {
                return Title;
            }

            set
            {
                Title = value;
            }
        }

        [XmlAttribute("parent")]
        public string Parent { get; set; }

        [XmlAttribute("title")]
        public string Title { get; set; }

        public override SubsonicModelTypeEnum Type
        {
            get
            {
                return IsDirectory
                           ? SubsonicModelTypeEnum.MusicDirectory
                           : IsVideo ? SubsonicModelTypeEnum.Video : SubsonicModelTypeEnum.Song;
            }
        }

        #endregion

        #region Public Methods and Operators

        public override Tuple<string, string> GetDescription()
        {
            return Type == SubsonicModelTypeEnum.MusicDirectory
                ? new Tuple<string, string>(Title, string.Empty)
                : new Tuple<string, string>(Title, Artist);
        }

        #endregion
    }
}
