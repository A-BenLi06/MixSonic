namespace Mixdrome.Phone.BackgroundAudioTask
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Mixdrome.Phone.Services;
    using Windows.ApplicationModel.Background;
    using Windows.Foundation.Collections;
    using Windows.Media;
    using Windows.Media.Playback;
    using Windows.Storage;
    using Windows.Storage.Streams;

    public sealed class AudioTask : IBackgroundTask
    {
        private readonly AudioQueueState queue = new AudioQueueState();
        private readonly SemaphoreSlim commands = new SemaphoreSlim(1, 1);
        private readonly object lifetime = new object();
        private int sourceGeneration;
        private BackgroundTaskDeferral deferral;
        private IBackgroundTaskInstance task;
        private SystemMediaTransportControls transport;
        private MediaPlayer player;
        private PhoneListeningReporter listening;
        private string version = string.Empty;
        private readonly string session = Guid.NewGuid().ToString("N");
        private int sequence;
        private int closed;

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            task = taskInstance;
            deferral = taskInstance.GetDeferral();
            taskInstance.Canceled += OnCanceled;
            taskInstance.Task.Completed += OnCompleted;
            player = BackgroundMediaPlayer.Current;
            listening = new PhoneListeningReporter(player);
            listening.Start();
            player.AutoPlay = true;
            player.CurrentStateChanged += OnStateChanged;
            player.MediaEnded += OnEnded;
            player.MediaFailed += OnFailed;
            transport = SystemMediaTransportControls.GetForCurrentView();
            transport.IsEnabled = true;
            transport.ButtonPressed += OnTransportButton;
            BackgroundMediaPlayer.MessageReceivedFromForeground += OnMessage;
            UpdateTransport();
            SendState(BackgroundAudioMessages.Ready);
        }

        private void OnCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason) { Shutdown(); }
        private void OnCompleted(BackgroundTaskRegistration sender, BackgroundTaskCompletedEventArgs args) { Shutdown(); }

        private async void Shutdown()
        {
            lock (lifetime)
            {
                if (Interlocked.Exchange(ref closed, 1) != 0) return;
                Interlocked.Increment(ref sourceGeneration);
            }
            BackgroundMediaPlayer.MessageReceivedFromForeground -= OnMessage;
            if (player != null)
            {
                player.CurrentStateChanged -= OnStateChanged;
                player.MediaEnded -= OnEnded;
                player.MediaFailed -= OnFailed;
            }
            if (transport != null) transport.ButtonPressed -= OnTransportButton;
            if (task != null)
            {
                task.Canceled -= OnCanceled;
                task.Task.Completed -= OnCompleted;
            }
            await commands.WaitAsync();
            try { if (listening != null) await listening.StopAsync(); }
            catch (Exception exception) { Debug.WriteLine("Phone listening shutdown failed: " + exception.GetType().Name); }
            finally
            {
                commands.Release();
                if (deferral != null) { deferral.Complete(); deferral = null; }
            }
        }

        private async void OnMessage(object sender, MediaPlayerDataReceivedEventArgs args)
        {
            await ExecuteAsync(async () =>
            {
                var data = args.Data;
                if (data.ContainsKey(BackgroundAudioMessages.ReplaceQueue))
                {
                    await ReplaceQueueAsync(data);
                    return;
                }
                if (data.ContainsKey(BackgroundAudioMessages.RequestState)) { SendState(BackgroundAudioMessages.Snapshot); return; }
                if (data.ContainsKey(BackgroundAudioMessages.Play)) Play();
                else if (data.ContainsKey(BackgroundAudioMessages.Pause)) player.Pause();
                else if (data.ContainsKey(BackgroundAudioMessages.Next)) Next(false);
                else if (data.ContainsKey(BackgroundAudioMessages.Previous)) Previous();
                else if (data.ContainsKey(BackgroundAudioMessages.SelectTrack))
                {
                    var index = GetInt(data, BackgroundAudioMessages.SelectTrack, -1);
                    if (queue.Select(index)) PlayCurrent();
                }
                else if (data.ContainsKey(BackgroundAudioMessages.Seek))
                {
                    listening.Observe();
                    var seconds = data[BackgroundAudioMessages.Seek] is double ? (double)data[BackgroundAudioMessages.Seek] : -1;
                    if (player.CanSeek && seconds >= 0 && !double.IsInfinity(seconds) && !double.IsNaN(seconds))
                        player.Position = TimeSpan.FromSeconds(Math.Min(seconds, player.NaturalDuration.TotalSeconds));
                }
                else if (data.ContainsKey(BackgroundAudioMessages.Shuffle))
                    queue.SetShuffle(data[BackgroundAudioMessages.Shuffle] is bool && (bool)data[BackgroundAudioMessages.Shuffle]);
                else if (data.ContainsKey(BackgroundAudioMessages.Repeat))
                    queue.SetRepeat(GetInt(data, BackgroundAudioMessages.Repeat, 0));
                UpdateTransport();
                SendState(BackgroundAudioMessages.StateChanged);
            });
        }

        private async Task ReplaceQueueAsync(ValueSet data)
        {
            var name = data.ContainsKey(BackgroundAudioMessages.QueueFile) ? data[BackgroundAudioMessages.QueueFile] as string : null;
            Guid token;
            if (name == null || !name.StartsWith(BackgroundAudioMessages.TransferFilePrefix, StringComparison.Ordinal)
                || !name.EndsWith(".json", StringComparison.Ordinal)
                || !Guid.TryParseExact(name.Substring(BackgroundAudioMessages.TransferFilePrefix.Length,
                    name.Length - BackgroundAudioMessages.TransferFilePrefix.Length - 5), "N", out token))
                throw new InvalidOperationException("Invalid queue transfer.");
            var file = await ApplicationData.Current.LocalFolder.GetFileAsync(name);
            List<TrackInfo> tracks;
            try
            {
                var properties = await file.GetBasicPropertiesAsync();
                if (properties.Size > 16 * 1024 * 1024) throw new InvalidOperationException("Queue transfer is too large.");
                tracks = AudioJson.Deserialize<List<TrackInfo>>(await FileIO.ReadTextAsync(file));
            }
            finally { await file.DeleteAsync(); }
            if (closed != 0) return;
            var index = GetInt(data, BackgroundAudioMessages.CurrentIndex, -1);
            // Validate before replacing the live queue or its durable snapshot.
            var candidate = new AudioQueueState();
            if (!candidate.Replace(tracks, index)) throw new InvalidOperationException("Invalid audio queue.");
            var nextVersion = Guid.NewGuid().ToString("N");
            var temporary = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                BackgroundAudioMessages.TransferFilePrefix + Guid.NewGuid().ToString("N") + ".json", CreationCollisionOption.FailIfExists);
            try
            {
                await FileIO.WriteTextAsync(temporary, AudioJson.Serialize(new AudioQueueSnapshot { Tracks = tracks, Version = nextVersion }));
                await temporary.RenameAsync(BackgroundAudioMessages.SessionFile, NameCollisionOption.ReplaceExisting);
            }
            catch { await temporary.DeleteAsync(); throw; }
            if (closed != 0) return;
            queue.Replace(tracks, index);
            version = nextVersion;
            PlayCurrent();
            SendState(BackgroundAudioMessages.Snapshot);
        }

        private async Task ExecuteAsync(Func<Task> action)
        {
            await commands.WaitAsync();
            try { if (closed == 0) await action(); }
            catch (Exception exception)
            {
                Debug.WriteLine("Audio operation failed: " + exception.GetType().Name);
                SendFailure("PlaybackFailed");
            }
            finally { commands.Release(); }
        }

        private static int GetInt(ValueSet data, string key, int fallback)
        {
            return data.ContainsKey(key) && data[key] is int ? (int)data[key] : fallback;
        }

        private void Play()
        {
            if (queue.CurrentIndex < 0) return;
            if (player.CurrentState == MediaPlayerState.Closed || player.CurrentState == MediaPlayerState.Stopped) PlayCurrent();
            else player.Play();
        }

        private void PlayCurrent()
        {
            lock (lifetime)
            {
                if (closed != 0) return;
                if (queue.CurrentIndex < 0 || queue.CurrentIndex >= queue.Tracks.Count) return;
                var track = queue.Tracks[queue.CurrentIndex];
                var source = Interlocked.Increment(ref sourceGeneration);
                listening.SelectTrack(track, source);
                transport.DisplayUpdater.ClearAll();
                transport.DisplayUpdater.Type = MediaPlaybackType.Music;
                transport.DisplayUpdater.MusicProperties.Title = track.Title ?? string.Empty;
                transport.DisplayUpdater.MusicProperties.Artist = track.Artist ?? string.Empty;
                transport.DisplayUpdater.MusicProperties.AlbumArtist = track.Artist ?? string.Empty;
                Uri artwork;
                if (Uri.TryCreate(track.CoverArt, UriKind.Absolute, out artwork))
                    transport.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromUri(artwork);
                transport.DisplayUpdater.Update();
                player.SetUriSource(new Uri(track.StreamUrl, UriKind.Absolute));
                UpdateTransport();
                SendState(BackgroundAudioMessages.TrackChanged);
            }
        }

        private void Next(bool ended)
        {
            if (queue.Next(ended)) PlayCurrent();
            else if (ended) { player.Pause(); UpdateTransport(); SendState(BackgroundAudioMessages.StateChanged); }
        }

        private void Previous()
        {
            if (player.CanSeek && player.Position.TotalSeconds > 3) player.Position = TimeSpan.Zero;
            else if (queue.Previous()) PlayCurrent();
        }

        private async void OnEnded(MediaPlayer sender, object args)
        {
            var source = Volatile.Read(ref sourceGeneration);
            listening.Finish(source);
            await ExecuteAsync(() => { if (source == Volatile.Read(ref sourceGeneration)) Next(true); return Task.FromResult(true); });
        }

        private async void OnStateChanged(MediaPlayer sender, object args)
        {
            var source = Volatile.Read(ref sourceGeneration);
            listening.Observe(true, source);
            await ExecuteAsync(() =>
            {
                if (source != Volatile.Read(ref sourceGeneration)) return Task.FromResult(true);
                UpdateTransport();
                SendState(BackgroundAudioMessages.StateChanged);
                return Task.FromResult(true);
            });
        }

        private async void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            var source = Volatile.Read(ref sourceGeneration);
            listening.Finish(source);
            await ExecuteAsync(() =>
            {
                if (source != Volatile.Read(ref sourceGeneration)) return Task.FromResult(true);
                transport.PlaybackStatus = MediaPlaybackStatus.Stopped;
                SendFailure("PlaybackFailed");
                return Task.FromResult(true);
            });
        }

        private async void OnTransportButton(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            await ExecuteAsync(() =>
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play: Play(); break;
                    case SystemMediaTransportControlsButton.Pause: player.Pause(); break;
                    case SystemMediaTransportControlsButton.Next: Next(false); break;
                    case SystemMediaTransportControlsButton.Previous: Previous(); break;
                }
                return Task.FromResult(true);
            });
        }

        private void UpdateTransport()
        {
            if (transport == null || player == null || closed != 0) return;
            transport.IsPlayEnabled = queue.CurrentIndex >= 0;
            transport.IsPauseEnabled = queue.CurrentIndex >= 0;
            transport.IsNextEnabled = queue.CanNext;
            transport.IsPreviousEnabled = queue.CurrentIndex >= 0;
            switch (player.CurrentState)
            {
                case MediaPlayerState.Playing: transport.PlaybackStatus = MediaPlaybackStatus.Playing; break;
                case MediaPlayerState.Paused: transport.PlaybackStatus = MediaPlaybackStatus.Paused; break;
                case MediaPlayerState.Stopped: transport.PlaybackStatus = MediaPlaybackStatus.Stopped; break;
                case MediaPlayerState.Closed: transport.PlaybackStatus = MediaPlaybackStatus.Closed; break;
                default: transport.PlaybackStatus = MediaPlaybackStatus.Changing; break;
            }
        }

        private void SendFailure(string code)
        {
            Send(new ValueSet
            {
                { BackgroundAudioMessages.Failure, code },
                { BackgroundAudioMessages.Session, session },
                { BackgroundAudioMessages.Sequence, ++sequence }
            });
        }

        private void SendState(string kind)
        {
            if (player == null || closed != 0) return;
            var data = new ValueSet
            {
                { kind, true },
                { BackgroundAudioMessages.PlayerState, player.CurrentState.ToString() },
                { BackgroundAudioMessages.QueueVersion, version },
                { BackgroundAudioMessages.Session, session },
                { BackgroundAudioMessages.Sequence, ++sequence },
                { BackgroundAudioMessages.CanNext, queue.CanNext },
                { BackgroundAudioMessages.CurrentIndex, queue.CurrentIndex },
                { BackgroundAudioMessages.Shuffle, queue.Shuffle },
                { BackgroundAudioMessages.Repeat, queue.Repeat }
            };
            Send(data);
        }

        private void Send(ValueSet data)
        {
            if (closed != 0) return;
            try { BackgroundMediaPlayer.SendMessageToForeground(data); }
            catch (Exception exception) { Debug.WriteLine("Audio foreground unavailable: " + exception.GetType().Name); }
        }
    }
}
