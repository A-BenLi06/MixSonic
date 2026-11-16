namespace Mixdrome.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.EventAggregatorMessages;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services;
    using Client.Common.Services.Navidrome;
    using Mixdrome.AudioPlayback;

    public interface IMediaQueueService
    {
        Task<IList<Song>> ResolveAsync(IEnumerable<ISubsonicModel> items);
        void Play(NavidromeApi api, IEnumerable<Song> songs, int index = 0, long positionMilliseconds = 0);
        void Append(NavidromeApi api, IEnumerable<Song> songs, bool next);
        void PlayLocal(IEnumerable<PlaylistItem> songs, int index = 0);
        void AppendLocal(IEnumerable<PlaylistItem> songs);
        Task SaveServerQueueAsync();
        Task RestoreServerQueueAsync();
    }

    public sealed class MediaQueueService : IMediaQueueService
    {
        private readonly INavidromeSessionService _session;
        private readonly IPlaylistManagementService _playlist;
        private readonly IEventAggregator _events;
        private readonly IAudioPlayerViewModel _audio;

        public MediaQueueService(INavidromeSessionService session, IPlaylistManagementService playlist, IEventAggregator events, IAudioPlayerViewModel audio)
        { _session = session; _playlist = playlist; _events = events; _audio = audio; }

        public async Task<IList<Song>> ResolveAsync(IEnumerable<ISubsonicModel> items)
        {
            await _session.ConnectAsync();
            var api = _session.CreateApi();
            var folder = _session.FolderId;
            var songs = new List<Song>();
            foreach (var item in items)
            {
                var song = item as Song;
                if (song != null) songs.Add(song);
                else if (item.Type == SubsonicModelTypeEnum.Song) songs.Add(await api.GetSong(item.Id).ReadAsync());
                else if (item.Type == SubsonicModelTypeEnum.Album) songs.AddRange((await api.GetAlbum(item.Id).ReadAsync()).Songs);
                else if (item.Type == SubsonicModelTypeEnum.Artist)
                {
                    var artist = await api.GetArtist(item.Id).ReadAsync();
                    foreach (var album in artist.Albums) songs.AddRange((await api.GetAlbum(album.Id).ReadAsync()).Songs);
                }
                else if (item.Type == SubsonicModelTypeEnum.Playlist) songs.AddRange((await api.GetPlaylist(item.Id).ReadAsync()).Entries);
                else if (item.Type == SubsonicModelTypeEnum.Genre)
                {
                    var offset = 0;
                    IList<Song> page;
                    do
                    {
                        page = await api.GetGenreSongs(item.Name, folder, offset, 100).ReadAsync();
                        songs.AddRange(page); offset += page.Count;
                    } while (page.Count == 100);
                }
            }
            if (api.AccountKey != _session.AccountKey || folder != _session.FolderId) throw new OperationCanceledException();
            return songs;
        }

        public void Play(NavidromeApi api, IEnumerable<Song> songs, int index = 0, long positionMilliseconds = 0)
        {
            if (api.AccountKey != _session.AccountKey) throw new OperationCanceledException();
            var queue = songs.Select(api.CreatePlaylistItem).ToList();
            if (queue.Count == 0) return;
            _events.PublishOnUIThread(new PlayQueueMessage { Queue = queue, StartIndex = index,
                Options = positionMilliseconds > 0 ? new PlaybackStartOptions { PositionMilliseconds = positionMilliseconds } : null });
        }

        public void Append(NavidromeApi api, IEnumerable<Song> songs, bool next)
        {
            if (api.AccountKey != _session.AccountKey) throw new OperationCanceledException();
            var queue = songs.Select(api.CreatePlaylistItem).ToList();
            if (next) _playlist.InsertNext(queue);
            else _events.PublishOnUIThread(new AddItemsMessage { Queue = queue });
        }

        public async Task SaveServerQueueAsync()
        {
            await _session.ConnectAsync();
            var api = _session.CreateApi();
            var queue = _playlist.Items.ToList();
            if (queue.Any(item => item.AccountKey != api.AccountKey || string.IsNullOrEmpty(item.SongId)))
                throw new InvalidOperationException(NavidromeText.Get("QueueContainsOtherMedia"));
            var index = _playlist.CurrentItem == null ? 0 : queue.IndexOf(_playlist.CurrentItem);
            var position = _playlist.CurrentItem == null ? 0 : (long)_audio.GetCurrentPosition().TotalMilliseconds;
            await api.SaveQueue(queue.Select(item => item.SongId), index, position, _session.Capabilities.Supports("indexBasedQueue")).ReadAsync();
        }

        public void PlayLocal(IEnumerable<PlaylistItem> songs, int index = 0)
        {
            var queue = songs.ToList();
            if (queue.Any(item => item.AccountKey != _session.AccountKey || item.Uri == null || item.Uri.Scheme != "ms-appdata")) throw new OperationCanceledException();
            if (queue.Count > 0) _events.PublishOnUIThread(new PlayQueueMessage { Queue = queue, StartIndex = index });
        }
        public void AppendLocal(IEnumerable<PlaylistItem> songs)
        {
            var queue = songs.ToList();
            if (queue.Any(item => item.AccountKey != _session.AccountKey || item.Uri == null || item.Uri.Scheme != "ms-appdata")) throw new OperationCanceledException();
            _events.PublishOnUIThread(new AddItemsMessage { Queue = queue });
        }

        public async Task RestoreServerQueueAsync()
        {
            await _session.ConnectAsync();
            var api = _session.CreateApi();
            var queue = await api.GetQueue(_session.Capabilities.Supports("indexBasedQueue")).ReadAsync();
            if (api.AccountKey != _session.AccountKey) return;
            if (queue.Songs.Count == 0) _playlist.Clear();
            else Play(api, queue.Songs, queue.CurrentIndex, queue.PositionMilliseconds);
        }
    }
}
