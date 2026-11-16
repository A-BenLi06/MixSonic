namespace Mixdrome.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Client.Common.Models.Navidrome;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.Navidrome;

    public sealed class NavidromeItem : PropertyChangedBase
    {
        private NavidromeItem _sourceItem;
        public NavidromeItem SelectionItem { get { return _sourceItem ?? this; } }
        public string LibraryGroupKey { get; private set; }

        // Each grouped occurrence needs an independent XAML/automation identity.
        // Its media and command/selection identity remain shared with the source.
        internal NavidromeItem InGroup(string key)
        {
            return new NavidromeItem {
                _sourceItem = SelectionItem, LibraryGroupKey = key,
                Media = Media, Bookmark = Bookmark, Share = Share, Download = Download,
                Ordinal = Ordinal, Title = Title, Subtitle = Subtitle, ImageUrl = ImageUrl,
                Kind = Kind, IsHero = IsHero, IsGenreAlbum = IsGenreAlbum
            };
        }

        public IMediaModel Media { get; set; }
        public Bookmark Bookmark { get; set; }
        public Share Share { get; set; }
        public int Ordinal { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string ImageUrl { get; set; }
        public string Kind { get; set; }
        public bool IsHero { get; set; }
        public bool IsGenreAlbum { get; set; }
        public string YearText { get { var album = Media as Album; return album == null || album.Year == 0 ? null : album.Year.ToString(CultureInfo.CurrentCulture); } }
        public OfflineDownloadItem Download { get; set; }
        public string Artist { get { return Song != null ? Song.Artist : Media is Album ? ((Album)Media).Artist : Media is Playlist ? ((Playlist)Media).Owner : Subtitle; } }
        public string Album { get { return Song != null ? Song.Album : null; } }
        public string CountText
        {
            get
            {
                var album = Media as Album; var artist = Media as ExpandedArtist; var playlist = Media as Playlist;
                if (artist != null) return string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("AlbumCount"), artist.AlbumCount);
                var count = album != null ? album.SongCount : playlist != null ? playlist.SongCount : 0;
                return count > 0 ? string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("TrackCount"), count) : null;
            }
        }
        public Song Song { get { return Media as Song; } }
        public string TrackNumberText { get { return string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("TrackNumber"), Song != null && Song.Track > 0 ? Song.Track : Ordinal + 1); } }
        public string DurationText { get { return Song == null ? null : FormatTime(Song.Duration); } }
        public bool IsStarred { get { return Media != null && !string.IsNullOrEmpty(((SubsonicModelBase)Media).Starred); } }
        public void UpdateStarred(bool value)
        {
            ((SubsonicModelBase)Media).Starred = value ? DateTimeOffset.Now.ToString("o") : null;
            NotifyOfPropertyChange(() => IsStarred);
        }

        public static NavidromeItem From(IMediaModel media, NavidromeApi api, int ordinal = 0)
        {
            var song = media as Song;
            var album = media as Album;
            var artist = media as ExpandedArtist;
            var playlist = media as Playlist;
            var genre = media as Genre;
            var radio = media as InternetRadioStation;
            var playing = media as NowPlayingEntry;
            var item = new NavidromeItem { Media = media, Ordinal = ordinal,
                Title = song == null ? media.Name : song.Title,
                ImageUrl = api.CoverArtUrl(media.CoverArt), Kind = NavidromeText.Get(media.Type.ToString()) };
            if (song != null) item.Subtitle = Join(string.IsNullOrWhiteSpace(song.DisplayArtist) ? song.Artist : song.DisplayArtist, song.Album, FormatTime(song.Duration));
            else if (album != null) item.Subtitle = Join(album.Artist, album.Year == 0 ? null : album.Year.ToString(CultureInfo.CurrentCulture),
                string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("TrackCount"), album.SongCount));
            else if (artist != null) item.Subtitle = string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("AlbumCount"), artist.AlbumCount);
            else if (playlist != null) item.Subtitle = Join(playlist.Owner, string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("TrackCount"), playlist.SongCount));
            else if (genre != null) item.Subtitle = string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("TrackCount"), genre.SongCount);
            else if (radio != null) item.Subtitle = radio.HomePageUrl;
            if (playing != null) item.Subtitle = Join(playing.Username, playing.PlayerName, playing.Artist, playing.Album,
                string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("MinutesAgo"), Math.Max(0, playing.MinutesAgo)),
                string.IsNullOrWhiteSpace(playing.PlaybackState) ? null : NavidromeText.Get("PlaybackState" + playing.PlaybackState),
                playing.PlaybackRate <= 0 ? null : FormatTime(playing.PositionMilliseconds / 1000.0));
            return item;
        }

        public static string Join(params string[] parts) { return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part))); }
        public static string FormatTime(double seconds)
        { var time = TimeSpan.FromSeconds(Math.Max(0, seconds)); return time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss"); }
    }

    public sealed class MetadataRow
    {
        public string Label { get; set; }
        public string Value { get; set; }
    }

    public sealed class NamedOption<T>
    {
        public T Value { get; set; }
        public string Name { get; set; }
    }

    public static class NavidromeRoute
    {
        public static string For(string kind, string id) { return kind + "|" + Uri.EscapeDataString(id ?? string.Empty); }
        public static string[] Parse(string route)
        {
            var parts = (route ?? "albums").Split(new[] { '|' }, 2);
            return new[] { parts[0], parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty };
        }
    }
}
