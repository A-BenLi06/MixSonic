using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Client.Common.EventAggregatorMessages;
using Client.Common.Models;
using Client.Common.Models.Subsonic;
using Client.Common.Services;
using Client.Common.Services.DataStructures.SubsonicService;
using Mixdrome.AudioPlayback;
using Mixdrome.Framework.Interfaces;
using Mixdrome.Navidrome;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

public static class NavidromeAudioTests
{
    private static int _assertions;
    private static void Check(bool value, string message) { _assertions++; if (!value) throw new Exception(message); }
    public static int Main()
    {
        try
        {
            Verify("preferences", Preferences); Verify("replay-gain", GainPreferences); Verify("lifecycle", Lifecycle);
            Verify("compatibility", Compatibility); Verify("resolution", () => Resolution().GetAwaiter().GetResult());
            Console.WriteLine("PASS: " + _assertions + " production audio assertions with simulated native events. Decoding, XAML and background audio remain unverified.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private static void Verify(string group, Action action) { action(); Console.WriteLine("ND-CHECK:audio:" + group); }
    private static void Preferences()
    {
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
        settings.Clear(); settings["AudioVolume"] = "invalid"; settings["AudioRate"] = double.NaN; settings["AudioMuted"] = "invalid";
        var audio = new AudioPlayerViewModel();
        Check(audio.Volume == 1 && audio.PlaybackRate == 1 && !audio.IsMuted, "Malformed preferences restore safe native defaults.");
        var controls = new Controls(); audio.PlayerControls = controls;
        audio.Volume = 0.25; audio.IsMuted = true; audio.PlaybackRate = 1.5;
        Check(controls.Volume == 0.25 && controls.IsMuted && controls.PlaybackRate == 1.5, "Preferences reach the real production control interface.");
        var restored = new AudioPlayerViewModel { PlayerControls = new Controls() };
        Check(restored.Volume == 0.25 && restored.IsMuted && restored.PlaybackRate == 1.5, "Native preferences persist across player construction.");
        restored.Volume = double.PositiveInfinity; restored.PlaybackRate = 0;
        Check(restored.Volume == 0.25 && restored.PlaybackRate == 1.5, "Invalid control values cannot replace persisted preferences.");
        restored.Volume = 2;
        Check(restored.Volume == 1, "Volume respects the native control range.");
        settings.Clear();
    }
    private static void GainPreferences()
    {
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
        settings.Clear(); settings["AudioReplayGainMode"] = 1.5; settings["AudioPreAmplification"] = "invalid";
        var controls = new Controls(); var events = new Events();
        var audio = new AudioPlayerViewModel { PlayerControls = controls, EventAggregator = events };
        Check(audio.GainMode == ReplayGainMode.None && audio.PreAmplification == 0, "Malformed normalization preferences use neutral defaults.");
        var metadata = new ReplayGain { TrackGain = -6, TrackGainSpecified = true, TrackPeak = 0.8, TrackPeakSpecified = true,
            AlbumGain = -3, AlbumGainSpecified = true, AlbumPeak = 0.5, AlbumPeakSpecified = true };
        var song = new PlaylistItem { ReplayGain = metadata, Uri = new Uri("ms-appdata:///local/gain.wav") };
        audio.Volume = 0.5; audio.Play(song);
        Check(controls.Volume == 0.5, "Off leaves the user's volume unchanged.");
        audio.GainMode = ReplayGainMode.Track;
        Check(Math.Abs(controls.Volume - 0.5 * Math.Pow(10, -6.0 / 20)) < 0.000001, "Track ReplayGain uses decibels and user volume.");
        audio.PreAmplification = 6;
        Check(Math.Abs(controls.Volume - 0.5) < 0.000001 && audio.Volume == 0.5, "Pre-amplification changes normalization without rewriting user volume.");
        audio.GainMode = ReplayGainMode.Album;
        Check(Math.Abs(controls.Volume - 0.5 * Math.Pow(10, 3.0 / 20)) < 0.000001, "Album mode selects its own gain and peak.");
        metadata.AlbumPeak = 1.4; audio.PreAmplification = 15;
        Check(Math.Abs(controls.Volume - 0.5 / 1.4) < 0.000001, "The peak caps amplification to prevent clipping.");
        metadata.AlbumGain = 20; metadata.AlbumPeak = 0.1; audio.PreAmplification = 0;
        Check(controls.Volume == 1 && audio.IsGainLimited && audio.Volume == 0.5, "Native volume limits stay explicit instead of corrupting the requested volume.");
        audio.Volume = 0.05;
        Check(Math.Abs(controls.Volume - 0.5) < 0.000001 && !audio.IsGainLimited, "Available user-volume headroom permits positive normalization.");
        var replacement = new Controls(); audio.PlayerControls = replacement;
        Check(Math.Abs(replacement.Volume - 0.5) < 0.000001, "Replacement native controls receive the current normalization.");
        var restored = new AudioPlayerViewModel();
        Check(restored.GainMode == ReplayGainMode.Album && restored.PreAmplification == 0 && restored.Volume == 0.05, "Normalization preferences persist independently from source metadata.");
        audio.PreAmplification = double.NaN; audio.GainMode = (ReplayGainMode)999;
        Check(audio.PreAmplification == 0 && audio.GainMode == ReplayGainMode.Album, "Invalid normalization input cannot replace saved preferences.");
        audio.Play(new PlaylistItem { Uri = new Uri("https://radio.example.com/live") });
        Check(replacement.Volume == 0.05 && !audio.IsGainLimited, "A new source without gain metadata resets normalization.");
        Check(ReplayGain.Calculate(new ReplayGain { TrackGainSpecified = true }, ReplayGainMode.Track, 0) == 1,
            "A missing peak remains distinct from a zero peak and cannot invent normalization.");
        Check(ReplayGain.Calculate(new ReplayGain { TrackGainSpecified = true, TrackPeakSpecified = true }, ReplayGainMode.Track, 0) == 1,
            "Explicit zero gain/peak remain valid metadata.");
        metadata.TrackGain = double.PositiveInfinity;
        Check(ReplayGain.Calculate(metadata, ReplayGainMode.Track, 0) == 1, "Non-finite server gain cannot reach native controls.");
        settings.Clear();
    }

    private static void Lifecycle()
    {
        var events = new Events(); var controls = new Controls();
        var audio = new AudioPlayerViewModel { EventAggregator = events, PlayerControls = controls };
        var song = new PlaylistItem { Title = "local", Duration = 20, Uri = new Uri("ms-appdata:///local/test.wav") };
        var stops = 0; audio.PlaybackStoped += (sender, args) => stops++;
        audio.Play(song, new PlaybackStartOptions { PositionMilliseconds = 1234 });
        Check(audio.IsBuffering && !audio.CanSeek && !audio.IsActuallyPlaying && !events.Messages.OfType<TrackPlaybackStartedMessage>().Any(), "A selected source is not a successful media start.");
        audio.MediaOpened();
        Check(audio.CanSeek && controls.Position.TotalMilliseconds == 1234 && controls.Plays == 1, "Bookmark milliseconds seek only after media opens.");
        State(audio, MediaElementState.Playing); State(audio, MediaElementState.Playing);
        Check(audio.IsActuallyPlaying && events.Messages.OfType<TrackPlaybackStartedMessage>().Count() == 1, "Repeated native Playing events report one occurrence.");
        audio.Seek(TimeSpan.FromSeconds(100)); Check(controls.Position.TotalSeconds == 20, "Seeking is clamped to the actual media duration.");
        audio.Pause(); State(audio, MediaElementState.Paused);
        Check(!audio.IsActuallyPlaying && controls.Pauses == 1, "Pause uses native controls and actual state.");
        audio.Resume(); State(audio, MediaElementState.Playing);
        Check(controls.Plays == 2 && events.Messages.OfType<TrackPlaybackStartedMessage>().Count() == 1, "Resume does not invent another listening occurrence.");
        controls.Position = TimeSpan.FromSeconds(7); audio.Stop(); audio.Stop(); State(audio, MediaElementState.Playing);
        Check(stops == 1 && audio.LastStoppedPosition.TotalSeconds == 7 && !audio.IsActuallyPlaying && !audio.CanSeek, "Stop preserves position and ignores duplicate/stale native state.");
        audio.SongEnded(); Check(!events.Messages.OfType<PlayNextMessage>().Any(), "An ended event after stop cannot skip a queue entry.");
        audio.Play(song); audio.Pause(); audio.MediaOpened();
        Check(controls.Pauses == 2, "A pause requested while opening survives MediaOpened.");
        audio.SongEnded(); audio.SongEnded();
        Check(events.Messages.OfType<PlayNextMessage>().Count() == 1, "Natural completion advances the queue exactly once.");
        audio.Play(song); audio.RestorePosition(5678); audio.MediaOpened();
        Check(controls.Position.TotalMilliseconds == 5678, "Frame-state resume can wait for MediaOpened.");
        audio.RestorePosition(long.MaxValue);
        Check(controls.Position.TotalSeconds == 20, "Restored positions respect the native duration without numeric overflow.");
    }
    private static void Compatibility()
    {
        var config = new SubsonicServiceConfiguration { BaseUrl = "http://localhost:10011/music/", Username = "fixture", Password = "fixture", StreamingFormat = "raw" };
        var subsonic = new SubsonicService { Configuration = config };
        var events = new Events(); var controls = new Controls();
        var audio = new AudioPlayerViewModel { EventAggregator = events, PlayerControls = controls, SubsonicService = subsonic };
        var song = new PlaylistItem { SongId = "id&音", AccountKey = AccountIdentity.For(config), Uri = subsonic.GetUriForFileWithId("id&音"), Duration = 20 };
        config.StreamingFormat = "mp3"; config.MaximumBitRate = 192;
        audio.Play(song);
        Check(Query(audio.Source, "format") == "mp3" && Query(audio.Source, "maxBitRate") == "192", "Queued media picks up changed stream preferences when it starts.");
        Fail(audio, "requested MP3 failed");
        Check(events.Messages.OfType<PlayFailedMessage>().Count() == 1 && !audio.IsUsingCompatibleStream, "An already requested MP3 fails without a retry loop.");
        config.StreamingFormat = "raw";
        audio.Play(song, new PlaybackStartOptions { PositionMilliseconds = 4321 });
        Fail(audio, "original cannot open");
        Check(audio.IsUsingCompatibleStream && audio.IsBuffering && Query(audio.Source, "format") == "mp3" && Query(audio.Source, "id") == song.SongId, "An unopened original retries once through a correctly encoded MP3 stream.");
        Check(!events.Messages.OfType<TrackPlaybackStartedMessage>().Any(), "A compatibility request is not a successful media start.");
        audio.MediaOpened(); Check(controls.Position.TotalMilliseconds == 4321, "Compatibility opening preserves an initial bookmark position.");
        State(audio, MediaElementState.Playing);
        Check(events.Messages.OfType<TrackPlaybackStartedMessage>().Count() == 1, "Only actual compatible playback starts listening history.");
        Fail(audio, "compatible failed");
        Check(events.Messages.OfType<PlayFailedMessage>().Count() == 2 && audio.PlaybackError.Contains("original cannot open") && audio.PlaybackError.Contains("compatible failed"), "Both failures remain visible when compatibility also fails.");
        audio.Play(song); audio.MediaOpened(); State(audio, MediaElementState.Playing); Fail(audio, "connection lost");
        Check(!audio.IsUsingCompatibleStream && events.Messages.OfType<PlayFailedMessage>().Count() == 3, "A failure after actual playback cannot create a second compatibility listen.");
        var radio = new PlaylistItem { Uri = new Uri("https://radio.example.com/live"), AccountKey = AccountIdentity.For(config) };
        audio.Play(radio); Fail(audio, "radio failed");
        Check(!audio.IsUsingCompatibleStream && events.Messages.OfType<PlayFailedMessage>().Count() == 4, "Radio URLs are not substituted with song transcoding.");
        var local = new PlaylistItem { SongId = song.SongId, AccountKey = song.AccountKey, Uri = new Uri("ms-appdata:///local/offline.wav") };
        audio.Play(local); Fail(audio, "local failed");
        Check(!audio.IsUsingCompatibleStream && events.Messages.OfType<PlayFailedMessage>().Count() == 5, "Offline playback failure does not require a server connection.");
        config.Username = "other";
        audio.Play(song); Fail(audio, "stale account failed");
        Check(!audio.IsUsingCompatibleStream && events.Messages.OfType<PlayFailedMessage>().Count() == 6, "An old account's song cannot retry using different credentials.");
    }
    private static async Task Resolution()
    {
        var config = new SubsonicServiceConfiguration { BaseUrl = "https://fixture.example/music/", Username = "fixture", Password = "fixture" };
        var service = new SubsonicService { Configuration = config }; var resolver = new Resolver(); var events = new Events(); var controls = new Controls();
        var audio = new AudioPlayerViewModel { SubsonicService = service, StreamService = resolver, EventAggregator = events, PlayerControls = controls };
        var first = new PlaylistItem { AccountKey = AccountIdentity.For(config), SongId = "first", Uri = service.GetUriForFileWithId("first") };
        var second = new PlaylistItem { AccountKey = first.AccountKey, SongId = "second", Uri = service.GetUriForFileWithId("second") };
        audio.Play(first, new PlaybackStartOptions { PositionMilliseconds = 1111 });
        audio.MediaOpened(); audio.SongEnded();
        State(audio, MediaElementState.Playing);
        Check(audio.Source == null && audio.IsBuffering && !audio.IsActuallyPlaying && controls.Plays == 0 && !events.Messages.OfType<PlayNextMessage>().Any() && !events.Messages.OfType<TrackPlaybackStartedMessage>().Any(), "Negotiation ignores stale media-open, completion and playing events.");
        audio.Pause(); audio.RestorePosition(5678);
        resolver.Requests[0].Completion.SetResult(new ResolvedAudioStream { Uri = new Uri("https://fixture.example/first-token"), Negotiated = true, Container = "wav" });
        await Until(() => audio.Source != null); audio.MediaOpened();
        Check(controls.Plays == 0 && controls.Pauses == 1 && controls.Position.TotalMilliseconds == 5678, "Pause and updated resume positions survive pending negotiation.");
        audio.Play(first); audio.Play(second);
        Check(resolver.Requests[1].Cancellation.IsCancellationRequested, "Skipping cancels the earlier resolution token.");
        resolver.Requests[2].Completion.SetResult(new ResolvedAudioStream { Uri = new Uri("https://fixture.example/second-token"), Negotiated = true, Transcoded = true, Container = "mp3" });
        await Until(() => audio.Source != null && audio.Source.AbsolutePath == "/second-token");
        resolver.Requests[1].Completion.SetResult(new ResolvedAudioStream { Uri = new Uri("https://fixture.example/late-token") }); await Task.Delay(10);
        Check(audio.Source.AbsolutePath == "/second-token" && audio.CurrentItem == second && !string.IsNullOrEmpty(audio.StreamInformation), "A late resolution cannot replace a newer occurrence or its stream information.");
        Check(second.Uri.AbsolutePath.EndsWith("stream.view"), "Negotiated tokens remain transient rather than entering persisted queue metadata.");
        audio.MediaOpened(); State(audio, MediaElementState.Playing); Fail(audio, "negotiated MP3 failed");
        Check(!audio.IsUsingCompatibleStream && events.Messages.OfType<PlayFailedMessage>().Count() == 1, "An already negotiated MP3 cannot enter a compatibility loop.");
        audio.Play(first); audio.Stop();
        resolver.Requests[3].Completion.SetResult(new ResolvedAudioStream { Uri = new Uri("https://fixture.example/stopped-token") }); await Task.Delay(10);
        Check(audio.Source == null && !audio.IsBuffering && resolver.Requests[3].Cancellation.IsCancellationRequested, "Stopping suppresses a resolver that completes after cancellation.");
        audio.Play(first); resolver.Requests[4].Completion.SetException(new InvalidOperationException("server decision failed"));
        await Until(() => !audio.IsBuffering);
        Check(audio.PlaybackError == "server decision failed" && events.Messages.OfType<PlayFailedMessage>().Count() == 2, "Decision failures stop buffering and retain the real error without inventing a listen.");
        audio.Play(first); config.Username = "another";
        resolver.Requests[5].Completion.SetResult(new ResolvedAudioStream { Uri = new Uri("https://fixture.example/other-account-token") });
        await Until(() => !audio.IsBuffering);
        Check(audio.Source == null && events.Messages.OfType<PlayFailedMessage>().Count() == 3, "An account change while resolving cannot open the returned stream.");
        config.Username = "fixture";
        audio.Play(first); config.Password = "new-credential";
        resolver.Requests[6].Completion.SetResult(new ResolvedAudioStream { Uri = new Uri("https://fixture.example/old-credential-token") });
        await Until(() => !audio.IsBuffering);
        Check(audio.Source == null && events.Messages.OfType<PlayFailedMessage>().Count() == 4, "A credential change before the player consumes a decision invalidates that source.");
        audio.Play(first); config.AuthenticationMode = SubsonicAuthenticationMode.ApiKey;
        resolver.Requests[7].Completion.SetResult(new ResolvedAudioStream { Uri = new Uri("https://fixture.example/old-authentication-token") });
        await Until(() => !audio.IsBuffering);
        Check(audio.Source == null && events.Messages.OfType<PlayFailedMessage>().Count() == 5, "An authentication-mode change before opening invalidates that source.");
    }
    private static async Task Until(Func<bool> complete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!complete()) { if (DateTime.UtcNow > deadline) throw new Exception("Controlled resolution did not complete."); await Task.Delay(5); }
    }
    private sealed class Resolver : INavidromeStreamService
    {
        public readonly List<Pending> Requests = new List<Pending>();
        public Task<ResolvedAudioStream> ResolveAsync(PlaylistItem item, CancellationToken cancellation)
        { var pending = new Pending { Cancellation = cancellation }; Requests.Add(pending); return pending.Completion.Task; }
        public sealed class Pending { public CancellationToken Cancellation; public readonly TaskCompletionSource<ResolvedAudioStream> Completion = new TaskCompletionSource<ResolvedAudioStream>(); }
    }

    private static string Query(Uri uri, string key)
    {
        return uri.Query.TrimStart('?').Split('&').Select(part => part.Split(new[] { '=' }, 2)).Where(part => Uri.UnescapeDataString(part[0]) == key)
            .Select(part => part.Length == 2 ? Uri.UnescapeDataString(part[1]) : "").FirstOrDefault();
    }
    private static void State(AudioPlayerViewModel audio, MediaElementState state) { audio.MediaStateChanged(new MediaElement { CurrentState = state }, new RoutedEventArgs()); }
    private static void Fail(AudioPlayerViewModel audio, string message) { audio.SongFailed(new ExceptionRoutedEventArgs { ErrorMessage = message }); }
    private sealed class Controls : IExtendedPlayerControls
    {
        public TimeSpan Position; public int Plays, Pauses;
        public bool CanSeek { get { return true; } }
        public double Volume { get; set; }
        public bool IsMuted { get; set; }
        public double PlaybackRate { get; set; }
        public void Play() { Plays++; }
        public void Pause() { Pauses++; }
        public void Stop() { Position = TimeSpan.Zero; }
        public void Seek(TimeSpan position) { Position = position; }
        public TimeSpan GetCurrentPosition() { return Position; }
        public TimeSpan GetCurrentDuration() { return TimeSpan.FromSeconds(20); }
    }
    private sealed class Events : IEventAggregator
    {
        public readonly List<object> Messages = new List<object>();
        public bool HandlerExistsFor(Type type) { return true; }
        public void Subscribe(object subscriber) { }
        public void Unsubscribe(object subscriber) { }
        public void Publish(object message, Action<Action> marshal) { Messages.Add(message); }
    }
}
