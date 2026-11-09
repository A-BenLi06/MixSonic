namespace Client.Common.Models.Subsonic
{
    using System.Xml.Serialization;

    public sealed class MediaArtist
    {
        [XmlAttribute("id")] public string Id { get; set; }
        [XmlAttribute("name")] public string Name { get; set; }
    }

    public sealed class MediaGenre
    {
        [XmlAttribute("name")] public string Name { get; set; }
    }

    public sealed class ReplayGain
    {
        [XmlAttribute("trackGain")] public double TrackGain { get; set; }
        [XmlIgnore] public bool TrackGainSpecified { get; set; }
        [XmlAttribute("albumGain")] public double AlbumGain { get; set; }
        [XmlIgnore] public bool AlbumGainSpecified { get; set; }
        [XmlAttribute("trackPeak")] public double TrackPeak { get; set; }
        [XmlIgnore] public bool TrackPeakSpecified { get; set; }
        [XmlAttribute("albumPeak")] public double AlbumPeak { get; set; }
        [XmlIgnore] public bool AlbumPeakSpecified { get; set; }
    }
}
