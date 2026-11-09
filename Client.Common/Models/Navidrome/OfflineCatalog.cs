namespace Client.Common.Models.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Client.Common.Models.Subsonic;

    public sealed class DownloadedSong
    {
        public DownloadedSong() { Lyrics = new List<LyricVariant>(); }
        public Song Song { get; set; }
        public string OriginalFile { get; set; }
        public long OriginalSize { get; set; }
        public string PlaybackFile { get; set; }
        public long PlaybackSize { get; set; }
        public string ArtworkFile { get; set; }
        public List<LyricVariant> Lyrics { get; set; }
        public DateTime CompletedUtc { get; set; }
        public string CacheError { get; set; }
    }

    public sealed class OfflineCatalog
    {
        public OfflineCatalog() { SchemaVersion = 1; Songs = new List<DownloadedSong>(); }
        public int SchemaVersion { get; set; }
        public string AccountKey { get; set; }
        public List<DownloadedSong> Songs { get; set; }
        public static bool IsFileName(string name)
        { return name != null && Regex.IsMatch(name, @"\A[0-9a-f]{32}-(original|playback|cover)\.[a-z0-9]{1,10}\z"); }
        public void Validate(string account)
        {
            if (SchemaVersion != 1 || AccountKey != account || Songs == null || Songs.Any(record => record == null || record.Song == null ||
                string.IsNullOrWhiteSpace(record.Song.Id) || !IsFileName(record.OriginalFile) || record.OriginalSize <= 0 ||
                (record.PlaybackFile != null && (!IsFileName(record.PlaybackFile) || record.PlaybackSize <= 0)) ||
                (record.ArtworkFile != null && !IsFileName(record.ArtworkFile)) || record.Lyrics == null ||
                record.Lyrics.Any(variant => variant == null || variant.Lines == null || variant.Lines.Any(line => line == null))) ||
                Songs.Select(record => record.Song.Id).Distinct().Count() != Songs.Count)
                throw new InvalidDataException("The offline library catalog is invalid.");
        }
        public static string Suffix(string suffix)
        { return !string.IsNullOrEmpty(suffix) && Regex.IsMatch(suffix, @"\A[a-zA-Z0-9]{1,10}\z") ? suffix.ToLowerInvariant() : "bin"; }
        public static string ExportName(Song song)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var name = new string((song.Title ?? song.Id ?? "audio").Select(ch => char.IsControl(ch) || invalid.Contains(ch) ? '_' : ch).ToArray()).Trim(' ', '.');
            if (name.Length > 120) name = name.Substring(0, 120).TrimEnd(' ', '.');
            if (string.IsNullOrEmpty(name)) name = "audio";
            if (Regex.IsMatch(name, @"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|\z)", RegexOptions.IgnoreCase)) name = "_" + name;
            return name;
        }
    }
}
