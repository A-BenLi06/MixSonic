namespace Client.Common.Models.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Client.Common.Models.Subsonic;

    public enum AlbumListType
    {
        AlphabeticalByName, AlphabeticalByArtist, Newest, Recent, Frequent,
        Random, Starred, Highest, ByGenre, ByYear
    }

    public sealed class AlbumQuery
    {
        public AlbumQuery() { Size = 100; }
        public AlbumListType Type { get; set; }
        public int Offset { get; set; }
        public int Size { get; set; }
        public string MusicFolderId { get; set; }
        public string Genre { get; set; }
        public int FromYear { get; set; }
        public int ToYear { get; set; }

        public string ApiType
        {
            get
            {
                switch (Type)
                {
                    case AlbumListType.AlphabeticalByName: return "alphabeticalByName";
                    case AlbumListType.AlphabeticalByArtist: return "alphabeticalByArtist";
                    case AlbumListType.Newest: return "newest";
                    case AlbumListType.Recent: return "recent";
                    case AlbumListType.Frequent: return "frequent";
                    case AlbumListType.Random: return "random";
                    case AlbumListType.Starred: return "starred";
                    case AlbumListType.Highest: return "highest";
                    case AlbumListType.ByGenre: return "byGenre";
                    case AlbumListType.ByYear: return "byYear";
                    default: throw new ArgumentOutOfRangeException("Type");
                }
            }
        }
    }

    public sealed class SearchQuery
    {
        public SearchQuery() { ArtistCount = AlbumCount = SongCount = 100; }
        public string Query { get; set; }
        public string MusicFolderId { get; set; }
        public int ArtistCount { get; set; }
        public int ArtistOffset { get; set; }
        public int AlbumCount { get; set; }
        public int AlbumOffset { get; set; }
        public int SongCount { get; set; }
        public int SongOffset { get; set; }
    }

    public sealed class ServerCapabilities
    {
        public ServerCapabilities() { Extensions = new Dictionary<string, IList<int>>(StringComparer.Ordinal); }
        public string Type { get; set; }
        public string ServerVersion { get; set; }
        public string ProtocolVersion { get; set; }
        public bool OpenSubsonic { get; set; }
        public IDictionary<string, IList<int>> Extensions { get; private set; }
        public bool Supports(string name, int version = 1)
        {
            IList<int> versions;
            return Extensions.TryGetValue(name, out versions) && versions.Contains(version);
        }
    }

    public sealed class ServerUser
    {
        public string Username { get; set; }
        public bool AdminRole { get; set; }
        public bool DownloadRole { get; set; }
        public bool ShareRole { get; set; }
        public bool StreamRole { get; set; }
        public bool JukeboxRole { get; set; }
        public bool ScrobblingEnabled { get; set; }
    }

    public sealed class ArtistInformation
    {
        public ArtistInformation() { SimilarArtists = new List<ExpandedArtist>(); }
        public string Biography { get; set; }
        public string MusicBrainzId { get; set; }
        public string LastFmUrl { get; set; }
        public string ImageUrl { get; set; }
        public IList<ExpandedArtist> SimilarArtists { get; set; }
    }

    public sealed class AlbumInformation
    {
        public string Notes { get; set; }
        public string MusicBrainzId { get; set; }
        public string LastFmUrl { get; set; }
        public string ImageUrl { get; set; }
    }

    public sealed class StarredMedia
    {
        public StarredMedia()
        {
            Artists = new List<ExpandedArtist>(); Albums = new List<Album>(); Songs = new List<Song>();
        }
        public IList<ExpandedArtist> Artists { get; set; }
        public IList<Album> Albums { get; set; }
        public IList<Song> Songs { get; set; }
    }

    public sealed class ServerPlayQueue
    {
        public ServerPlayQueue() { Songs = new List<Song>(); CurrentIndex = -1; }
        public IList<Song> Songs { get; set; }
        public int CurrentIndex { get; set; }
        public long PositionMilliseconds { get; set; }
        public string ChangedBy { get; set; }
        public string Changed { get; set; }
    }

    public sealed class Bookmark
    {
        public Song Song { get; set; }
        public long PositionMilliseconds { get; set; }
        public string Comment { get; set; }
    }

    public sealed class Share
    {
        public string Id { get; set; }
        public string Url { get; set; }
        public string Description { get; set; }
        public string Username { get; set; }
        public DateTimeOffset? Expires { get; set; }
        public int VisitCount { get; set; }
    }

    public sealed class ScanStatus
    {
        public bool Scanning { get; set; }
        public long Count { get; set; }
        public string LastScan { get; set; }
    }

    public sealed class LyricsLine
    {
        public long? StartMilliseconds { get; set; }
        public string Text { get; set; }
    }

    public sealed class LyricVariant
    {
        public LyricVariant() { Lines = new List<LyricsLine>(); }
        public string Language { get; set; }
        public string Kind { get; set; }
        public string DisplayTitle { get; set; }
        public string DisplayArtist { get; set; }
        public bool Synced { get; set; }
        public long OffsetMilliseconds { get; set; }
        public IList<LyricsLine> Lines { get; set; }

        public int LineAt(long positionMilliseconds)
        {
            if (!Synced) return -1;
            var time = positionMilliseconds - OffsetMilliseconds;
            var index = -1;
            for (var i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].StartMilliseconds.HasValue && Lines[i].StartMilliseconds.Value <= time &&
                    (index < 0 || Lines[i].StartMilliseconds.Value >= Lines[index].StartMilliseconds.Value)) index = i;
            }
            return index;
        }
    }

    public sealed class JukeboxState
    {
        public JukeboxState() { Songs = new List<Song>(); }
        public bool Playing { get; set; }
        public int CurrentIndex { get; set; }
        public double Gain { get; set; }
        public int Position { get; set; }
        public IList<Song> Songs { get; set; }
    }
}
