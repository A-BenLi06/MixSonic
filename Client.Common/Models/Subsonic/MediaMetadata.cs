namespace Client.Common.Models.Subsonic
{
    using System;
    using System.Xml.Serialization;

    public enum ReplayGainMode { None, Track, Album }

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
        [XmlAttribute("baseGain")] public double BaseGain { get; set; }
        [XmlIgnore] public bool BaseGainSpecified { get; set; }
        [XmlAttribute("fallbackGain")] public double FallbackGain { get; set; }
        [XmlIgnore] public bool FallbackGainSpecified { get; set; }

        public static double Calculate(ReplayGain metadata, ReplayGainMode mode, double preAmplification)
        {
            if (metadata == null || mode == ReplayGainMode.None) return 1;
            var album = mode == ReplayGainMode.Album;
            if (mode != ReplayGainMode.Track && !album) return 1;
            if (!(album ? metadata.AlbumGainSpecified && metadata.AlbumPeakSpecified : metadata.TrackGainSpecified && metadata.TrackPeakSpecified)) return 1;
            var gain = album ? metadata.AlbumGain : metadata.TrackGain;
            var peak = album ? metadata.AlbumPeak : metadata.TrackPeak;
            if (!Finite(gain) || !Finite(peak) || peak < 0 || !Finite(preAmplification)) return 1;
            var scale = Math.Pow(10, (gain + preAmplification) / 20);
            if (!Finite(scale)) return 1;
            // Navidrome caps the requested scale at the reciprocal peak to prevent clipping.
            return peak > 0 ? Math.Min(scale, 1 / peak) : scale;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
