namespace Subsonic8.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Client.Common.Models.Subsonic;

    // Headers share the wrap grid with media; each index entry points to the
    // exact header object in that grid, including after paging or filtering.
    public sealed class LibraryIndexGroup
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Subtitle { get { return Title; } }
        public bool IsSong { get; set; }
        public bool IsArtist { get; set; }
        public bool IsNamed { get; set; }
        public bool IsEnabled { get { return Items.Count > 0; } }
        public IList<NavidromeItem> Items { get; private set; }
        public LibraryIndexGroup() { Items = new List<NavidromeItem>(); }
    }

    public sealed class LibraryIndex
    {
        public IList<LibraryIndexGroup> Groups { get; private set; }
        public IList<object> Entries { get; private set; }

        public static string Initial(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "#";
            var first = StringInfo.GetNextTextElement(title.Trim());
            if (!char.IsLetter(first, 0)) return "#";
            // Fold accented Latin initials without transliterating other scripts.
            for (var latin = 'a'; latin <= 'z'; latin++)
                if (CultureInfo.CurrentCulture.CompareInfo.Compare(first, latin.ToString(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0)
                    return latin.ToString();
            return first.ToLowerInvariant();
        }

        public static LibraryIndex Create(IEnumerable<NavidromeItem> source, string section, string unknownArtist, string unknownGenre)
        {
            var named = section == "artists" || section == "genres";
            var groups = new Dictionary<string, LibraryIndexGroup>(StringComparer.Ordinal);
            Action<string, string, NavidromeItem> add = (key, title, item) =>
            {
                LibraryIndexGroup group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new LibraryIndexGroup { Key = key, Title = title, IsSong = section == "songs", IsArtist = section == "artists", IsNamed = named };
                    groups.Add(key, group);
                }
                if (item != null && !group.Items.Contains(item)) group.Items.Add(item);
            };
            if (!named)
            {
                add("#", "#", null);
                for (var letter = 'a'; letter <= 'z'; letter++) add(letter.ToString(), letter.ToString(), null);
            }
            foreach (var item in source)
            {
                var album = item.Media as Album;
                if (section == "artists" && album != null)
                {
                    // Album artists own an album grouping, not the individual track artists.
                    var artists = (album.Artists ?? new List<MediaArtist>()).Where(artist => !string.IsNullOrWhiteSpace(artist.Name)).ToList();
                    if (artists.Count == 0)
                        add(ArtistKey(album.ArtistId, album.Artist), string.IsNullOrWhiteSpace(album.Artist) ? unknownArtist : album.Artist, item);
                    else foreach (var artist in artists) add(ArtistKey(artist.Id, artist.Name), artist.Name, item);
                }
                else if (section == "genres" && album != null)
                {
                    var genres = (album.Genres ?? new List<MediaGenre>()).Select(genre => genre.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
                    if (genres.Count == 0) genres.Add(album.Genre);
                    foreach (var genre in genres) add("genre:" + (string.IsNullOrWhiteSpace(genre) ? string.Empty : genre.ToUpperInvariant()), string.IsNullOrWhiteSpace(genre) ? unknownGenre : genre, item);
                }
                else { var initial = Initial(item.Title); add(initial, initial, item); }
            }
            var ordered = named ? groups.Values.OrderBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase) :
                groups.Values.OrderBy(group => group.Key == "#" ? 0 : group.Key.Length == 1 && group.Key[0] >= 'a' && group.Key[0] <= 'z' ? 1 : 2)
                    .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase);
            var result = new LibraryIndex { Groups = ordered.ToList(), Entries = new List<object>() };
            foreach (var group in result.Groups.Where(group => group.IsEnabled))
            {
                var sorted = group.Items.OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Media.Id, StringComparer.Ordinal).ToList();
                group.Items.Clear();
                foreach (var item in sorted) group.Items.Add(item);
                result.Entries.Add(group);
                foreach (var item in group.Items) result.Entries.Add(item);
            }
            return result;
        }

        private static string ArtistKey(string id, string name)
        {
            return !string.IsNullOrWhiteSpace(id) ? "artist-id:" + id : "artist-name:" + (string.IsNullOrWhiteSpace(name) ? string.Empty : name.ToUpperInvariant());
        }
    }
}
