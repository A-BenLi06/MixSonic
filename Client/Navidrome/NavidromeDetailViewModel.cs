namespace Subsonic8.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.Navidrome;
    using MugenInjection.Attributes;
    using Subsonic8.AudioPlayback;
    using Subsonic8.Playback;
    using Windows.Data.Html;
    using Windows.System;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;

    public interface INavidromeDetailViewModel : IScreen { }
    public sealed class NavidromeDetailViewModel : NavidromeMediaPageViewModel, INavidromeDetailViewModel
    {
        private string _parameter;
        private NavidromeItem _current;
        private NavidromeApi _loadedApi;
        private string _externalUrl;

        public NavidromeDetailViewModel()
        {
            Tracks = new BindableCollection<NavidromeItem>(); Related = new BindableCollection<NavidromeItem>();
            Metadata = new BindableCollection<MetadataRow>(); SelectedItems = new ObservableCollection<object>();
            SelectedItems.CollectionChanged += (sender, args) => Changed();
            UpdateDisplayName = () => { };
        }
        [Inject] public IAudioPlayerViewModel Audio { get; set; }
        public string Parameter { get { return _parameter; } set { _parameter = value; } }
        public BindableCollection<NavidromeItem> Tracks { get; private set; }
        public BindableCollection<NavidromeItem> Related { get; private set; }
        public BindableCollection<MetadataRow> Metadata { get; private set; }
        public ObservableCollection<object> SelectedItems { get; private set; }
        public NavidromeItem Current { get { return _current; } }
        public string Notes { get; private set; }
        public string ImageUrl { get; private set; }
        public bool HasNotes { get { return !string.IsNullOrEmpty(Notes); } }
        public bool HasTracks { get { return Tracks.Count > 0; } }
        public bool HasRelated { get { return Related.Count > 0; } }
        public bool CanOpenExternal { get { Uri uri; return NavidromeBrowserViewModel.TryWebLink(_externalUrl, out uri); } }
        private Playlist Playlist { get { return _current == null ? null : _current.Media as Playlist; } }
        private bool OwnsPlaylist { get { return Playlist != null && Session.User != null && Playlist.Owner == Session.User.Username; } }
        public bool CanEditPlaylist { get { return !IsWorking && !IsLoading && OwnsPlaylist; } }
        public bool CanDeletePlaylist { get { return CanEditPlaylist; } }
        public bool CanRemoveTracks { get { return CanEditPlaylist && !Playlist.ReadOnly && SelectedItems.Count > 0; } }
        public bool CanMoveUp { get { return CanRemoveTracks && SelectedItems.Count == 1 && ((NavidromeItem)SelectedItems[0]).Ordinal > 0; } }
        public bool CanMoveDown { get { return CanRemoveTracks && SelectedItems.Count == 1 && ((NavidromeItem)SelectedItems[0]).Ordinal < Tracks.Count - 1; } }
        public bool CanArtistMix { get { return !IsLoading && !IsWorking && _current != null && (_current.Media is Song || _current.Media is ExpandedArtist); } }
        public bool CanTopSongs { get { return CanArtistMix; } }
        public bool CanSonicMix { get { return CanBookmark && !IsLoading && _loadedApi != null && _loadedApi.AccountKey == Session.AccountKey; } }
        public bool CanSonicPath { get { return CanSonicMix && PlaylistQueue.Items.Any(item => item.AccountKey == Session.AccountKey && !string.IsNullOrEmpty(item.SongId) && item.SongId != _current.Song.Id); } }
        public bool CanBookmark { get { return !IsWorking && _current != null && _current.Song != null; } }
        protected override IList<NavidromeItem> CommandItems
        { get { return SelectedItems.Count > 0 ? SelectedItems.Cast<NavidromeItem>().ToList() : _current == null ? new List<NavidromeItem>() : new List<NavidromeItem> { _current }; } }

        protected override void OnActivate() { PlaylistQueue.PropertyChanged += PlaylistQueueChanged; base.OnActivate(); }
        protected override void OnDeactivate(bool close) { PlaylistQueue.PropertyChanged -= PlaylistQueueChanged; base.OnDeactivate(close); }
        private void PlaylistQueueChanged(object sender, PropertyChangedEventArgs args) { NotifyOfPropertyChange(() => CanSonicPath); }
        protected override void Changed()
        {
            base.Changed();
            foreach (var property in new[] { "Current", "Notes", "ImageUrl", "HasNotes", "HasTracks", "HasRelated", "CanOpenExternal", "CanEditPlaylist",
                "CanDeletePlaylist", "CanRemoveTracks", "CanMoveUp", "CanMoveDown", "CanArtistMix", "CanTopSongs", "CanBookmark", "CanSonicMix", "CanSonicPath" }) NotifyOfPropertyChange(property);
        }

        public override async Task Reload()
        {
            var version = ++LoadVersion; IsLoading = true; Error = null;
            Tracks.Clear(); Related.Clear(); Metadata.Clear(); SelectedItems.Clear(); _current = null; Notes = null; _externalUrl = null;
            try
            {
                await Session.ConnectAsync();
                var api = Session.CreateApi(); var route = NavidromeRoute.Parse(Parameter);
                IMediaModel media;
                switch (route[0])
                {
                    case "album": media = await api.GetAlbum(route[1]).ReadAsync(); break;
                    case "artist": media = await api.GetArtist(route[1]).ReadAsync(); break;
                    case "playlist": media = await api.GetPlaylist(route[1]).ReadAsync(); break;
                    case "song": media = await api.GetSong(route[1]).ReadAsync(); break;
                    default: throw new InvalidOperationException(NavidromeText.Get("UnknownSection"));
                }
                if (!IsCurrent(version, api)) return;
                if (media == null) throw new InvalidOperationException(NavidromeText.Get("ItemUnavailable"));
                _loadedApi = api; _current = NavidromeItem.From(media, api); ImageUrl = _current.ImageUrl; DisplayName = _current.Title;
                var album = media as Album; var artist = media as ExpandedArtist; var playlist = media as Playlist; var song = media as Song;
                if (album != null) { AddTracks(album.Songs, api); AddAlbumMetadata(album); AddArtistLinks(album.Artists, album.ArtistId, album.Artist, api); }
                else if (artist != null) { Related.AddRange(artist.Albums.Select(item => NavidromeItem.From(item, api))); AddMetadata("AlbumCountLabel", artist.AlbumCount); }
                else if (playlist != null)
                { AddTracks(playlist.Entries, api); AddMetadata("Owner", playlist.Owner); AddMetadata("Comment", playlist.Comment);
                    AddMetadata("Public", NavidromeText.Get(playlist.Public ? "Yes" : "No"));
                    if (playlist.ReadOnly) AddMetadata("Playlist", NavidromeText.Get("ReadOnlyPlaylist")); }
                else if (song != null)
                {
                    AddTracks(new[] { song }, api); AddSongMetadata(song);
                    AddArtistLinks(song.Artists, song.ArtistId, song.Artist, api);
                    if (!string.IsNullOrEmpty(song.AlbumId)) Related.Add(NavidromeItem.From(new Album { Id = song.AlbumId, Name = song.Album, Artist = song.Artist, CoverArt = song.CoverArt }, api));
                }
                AddMetadata("Rating", ((SubsonicModelBase)media).UserRating);
                Changed();
                // Optional server information enriches the available media instead of replacing it.
                if (artist != null)
                {
                    var information = await api.GetArtistInfo(artist.Id).ReadAsync();
                    if (!IsCurrent(version, api)) return;
                    Notes = PlainText(information.Biography); _externalUrl = information.LastFmUrl;
                    AddMetadata("MusicBrainzId", information.MusicBrainzId);
                    Related.AddRange(information.SimilarArtists.Select(item => NavidromeItem.From(item, api)));
                    Uri image; if (string.IsNullOrEmpty(media.CoverArt) && NavidromeBrowserViewModel.TryWebLink(information.ImageUrl, out image)) ImageUrl = image.AbsoluteUri;
                }
                else if (album != null)
                {
                    var information = await api.GetAlbumInfo(album.Id).ReadAsync();
                    if (!IsCurrent(version, api)) return;
                    Notes = PlainText(information.Notes); _externalUrl = information.LastFmUrl;
                    AddMetadata("MusicBrainzId", information.MusicBrainzId);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (version == LoadVersion) Error = exception.Message; }
            finally { if (version == LoadVersion) { IsLoading = false; Changed(); } }
        }

        private static string PlainText(string text) { return string.IsNullOrWhiteSpace(text) ? null : HtmlUtilities.ConvertToText(text); }
        private void AddTracks(IEnumerable<Song> songs, NavidromeApi api)
        { Tracks.AddRange(songs.Select((song, index) => NavidromeItem.From(song, api, index))); }
        private void AddMetadata(string key, object value)
        { if (value != null && !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.CurrentCulture))) Metadata.Add(new MetadataRow { Label = NavidromeText.Get(key), Value = Convert.ToString(value, CultureInfo.CurrentCulture) }); }
        private void AddArtistLinks(IList<MediaArtist> artists, string id, string name, NavidromeApi api)
        {
            if (artists != null && artists.Count > 0)
                Related.AddRange(artists.Where(artist => !string.IsNullOrEmpty(artist.Id)).Select(artist => NavidromeItem.From(new ExpandedArtist { Id = artist.Id, Name = artist.Name }, api)));
            else if (!string.IsNullOrEmpty(id)) Related.Add(NavidromeItem.From(new ExpandedArtist { Id = id, Name = name }, api));
        }
        private void AddAlbumMetadata(Album album)
        {
            AddMetadata("Artist", album.Artist); if (album.Year != 0) AddMetadata("Year", album.Year);
            AddMetadata("Genre", album.Genres != null && album.Genres.Count > 0 ? string.Join(", ", album.Genres.Select(genre => genre.Name)) : album.Genre);
            AddMetadata("Duration", NavidromeItem.FormatTime(album.Duration)); AddMetadata("MusicBrainzId", album.MusicBrainzId);
        }
        private void AddSongMetadata(Song song)
        {
            AddMetadata("Artist", song.Artist); AddMetadata("Album", song.Album);
            if (song.Track != 0) AddMetadata("Track", song.Track); if (song.DiscNumber != 0) AddMetadata("Disc", song.DiscNumber);
            if (song.Year != 0) AddMetadata("Year", song.Year);
            AddMetadata("Genre", song.Genres != null && song.Genres.Count > 0 ? string.Join(", ", song.Genres.Select(genre => genre.Name)) : song.Genre);
            AddMetadata("Duration", NavidromeItem.FormatTime(song.Duration)); AddMetadata("Format", song.Suffix); AddMetadata("ContentType", song.ContentType);
            if (song.BitRate > 0) AddMetadata("BitRate", string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("Kbps"), song.BitRate));
            if (song.SamplingRate > 0) AddMetadata("SamplingRate", string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("Hz"), song.SamplingRate));
            if (song.BitDepth > 0) AddMetadata("BitDepth", song.BitDepth); if (song.ChannelCount > 0) AddMetadata("Channels", song.ChannelCount);
            if (song.Size > 0) AddMetadata("FileSize", string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("Bytes"), song.Size));
            AddMetadata("Path", song.Path); AddMetadata("MusicBrainzId", song.MusicBrainzId);
        }

        public void OpenRelated(ItemClickEventArgs args)
        {
            var item = (NavidromeItem)args.ClickedItem;
            NavigationService.NavigateToViewModel<NavidromeDetailViewModel>(NavidromeRoute.For(item.Media.Type.ToString().ToLowerInvariant(), item.Media.Id));
        }
        public void PlayTrack(ItemClickEventArgs args)
        {
            if (_loadedApi == null || _loadedApi.AccountKey != Session.AccountKey) return;
            var item = (NavidromeItem)args.ClickedItem;
            MediaQueue.Play(_loadedApi, Tracks.Select(track => track.Song), item.Ordinal);
            NavigationService.NavigateToViewModel<NavidromePlayerViewModel>();
        }
        public async Task OpenExternal() { Uri link; if (NavidromeBrowserViewModel.TryWebLink(_externalUrl, out link)) await Launcher.LaunchUriAsync(link); }
        public Task ArtistMix() { return Discovery(false); }
        public Task TopSongs() { return Discovery(true); }
        public Task SonicMix() { return AcousticDiscovery(null); }
        public Task SonicPath(FrameworkElement anchor) { return AcousticDiscovery(anchor); }
        private Task AcousticDiscovery(FrameworkElement anchor)
        {
            if (!CanSonicMix) return Task.FromResult(false);
            var song = _current.Song; var account = _loadedApi.AccountKey; var version = LoadVersion;
            return RunOperation(async api =>
            {
                if (!IsCurrent(version, api) || api.AccountKey != account) throw new OperationCanceledException();
                string destination = null;
                if (anchor != null)
                {
                    var choices = PlaylistQueue.Items.Where(item => item.AccountKey == api.AccountKey && !string.IsNullOrEmpty(item.SongId) && item.SongId != song.Id).ToList();
                    if (choices.Count == 0) { Error = NavidromeText.Get("SonicDestinationRequired"); return; }
                    var target = new FormField { LabelKey = "DestinationTrack", Kind = FormFieldKind.Choice, Choices = choices.Cast<object>(), DisplayMemberPath = "Title", Value = choices.First() };
                    if (!await Dialogs.FormAsync(anchor, "SonicPath", new[] { target })) return;
                    destination = ((PlaylistItem)target.Value).SongId;
                }
                if (!IsCurrent(version, api)) throw new OperationCanceledException();
                var songs = await (destination == null ? api.GetSonicSimilarSongs(song.Id) : api.FindSonicPath(song.Id, destination)).ReadAsync();
                if (!IsCurrent(version, api)) throw new OperationCanceledException();
                if (songs.Count == 0) { Error = NavidromeText.Get("NoDiscoverySongs"); return; }
                MediaQueue.Play(api, songs); NavigationService.NavigateToViewModel<NavidromePlayerViewModel>();
            });
        }
        private Task Discovery(bool top)
        {
            var media = _current.Media;
            return RunOperation(async api =>
            {
                var song = media as Song;
                var songs = top ? await api.GetTopSongs(song == null ? media.Name : song.Artist, song == null ? media.Id : song.ArtistId).ReadAsync()
                    : await api.GetSimilarSongs(media.Id).ReadAsync();
                RequireAccount(api);
                if (songs.Count == 0) { Error = NavidromeText.Get("NoDiscoverySongs"); return; }
                MediaQueue.Play(api, songs); NavigationService.NavigateToViewModel<NavidromePlayerViewModel>();
            });
        }

        public Task EditPlaylist(FrameworkElement anchor)
        {
            var playlist = Playlist;
            return RunOperation(async api =>
            {
                if (!OwnsPlaylist) return;
                var name = new FormField { LabelKey = "Name", Value = playlist.Name };
                var comment = new FormField { LabelKey = "Comment", Value = playlist.Comment };
                var isPublic = new FormField { LabelKey = "Public", Kind = FormFieldKind.Boolean, Value = playlist.Public };
                if (!await Dialogs.FormAsync(anchor, "EditPlaylist", new[] { name, comment, isPublic },
                    () => string.IsNullOrWhiteSpace(name.Value as string) ? NavidromeText.Get("NameRequired") : null)) return;
                RequireAccount(api); await api.UpdatePlaylist(playlist.Id, (string)name.Value, (string)comment.Value, (bool)isPublic.Value, null, null).ReadAsync();
                await Reload();
            });
        }
        public Task DeletePlaylist()
        {
            var playlist = Playlist;
            return RunOperation(async api =>
            {
                if (!OwnsPlaylist || !await Dialogs.ConfirmAsync("ConfirmDeletePlaylist")) return;
                RequireAccount(api); await api.DeletePlaylist(playlist.Id).ReadAsync(); RequireAccount(api);
                NavigationService.NavigateToViewModel<NavidromeBrowserViewModel>("playlists");
            });
        }
        public Task RemoveTracks()
        {
            var indices = SelectedItems.Cast<NavidromeItem>().Select(item => item.Ordinal).ToList(); var playlist = Playlist;
            return RunOperation(async api =>
            {
                if (!OwnsPlaylist || playlist.ReadOnly || !await Dialogs.ConfirmAsync("ConfirmRemoveTracks")) return;
                RequireAccount(api); await api.UpdatePlaylist(playlist.Id, null, null, null, null, indices).ReadAsync(); await Reload();
            });
        }
        public Task MoveUp() { return MoveTrack(-1); }
        public Task MoveDown() { return MoveTrack(1); }
        private Task MoveTrack(int delta)
        {
            var track = SelectedItems.Cast<NavidromeItem>().Single(); var playlist = Playlist; var songs = Tracks.Select(item => item.Song).ToList();
            return RunOperation(async api =>
            {
                if (!OwnsPlaylist || playlist.ReadOnly) return;
                var target = track.Ordinal + delta; if (target < 0 || target >= songs.Count) return;
                songs.RemoveAt(track.Ordinal); songs.Insert(target, track.Song);
                RequireAccount(api); await api.SavePlaylist(null, songs.Select(song => song.Id), playlist.Id).ReadAsync();
                await Reload(); if (target < Tracks.Count) SelectedItems.Add(Tracks[target]);
            });
        }
        public Task Bookmark(FrameworkElement anchor)
        {
            var song = _current.Song;
            return RunOperation(async api =>
            {
                var position = Audio.CurrentItem != null && Audio.CurrentItem.AccountKey == api.AccountKey && Audio.CurrentItem.SongId == song.Id
                    ? (long)Audio.GetCurrentPosition().TotalMilliseconds : 0;
                var comment = new FormField { LabelKey = "Comment" };
                if (!await Dialogs.FormAsync(anchor, "SaveBookmark", new[] { comment })) return;
                RequireAccount(api); await api.SaveBookmark(song.Id, position, comment.Value as string).ReadAsync();
            });
        }
    }
}
