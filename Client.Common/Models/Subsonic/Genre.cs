namespace Client.Common.Models.Subsonic
{
    using System;
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "genre", Namespace = "http://subsonic.org/restapi")]
    public class Genre : MediaModelBase
    {
        #region Public Properties

        [XmlAttribute("albumCount")]
        public int AlbumCount { get; set; }

        [XmlAttribute("songCount")]
        public int SongCount { get; set; }

        // XmlSerializer forbids hiding an inherited serializable member with a different form
        // (an [XmlText] override of the base [XmlAttribute("name")]), so the element text lives here.
        [XmlText]
        public string Value { get; set; }

        [XmlIgnore]
        public override string Name
        {
            get
            {
                return Value;
            }

            set
            {
                Value = value;
            }
        }

        [XmlIgnore]
        public override string CoverArt
        {
            get
            {
                return base.CoverArt;
            }

            set
            {
                base.CoverArt = value;
            }
        }

        public override SubsonicModelTypeEnum Type
        {
            get
            {
                return SubsonicModelTypeEnum.Genre;
            }
        }

        #endregion

        #region Public Methods and Operators

        public override Tuple<string, string> GetDescription()
        {
            return new Tuple<string, string>(Name, string.Format("{0} songs, {1} albums", SongCount, AlbumCount));
        }

        #endregion
    }
}
