using System;
using System.Collections.Generic;
using System.Linq;
using Caliburn.Micro;
using Client.Common.EventAggregatorMessages;
using Client.Common.Models;
using Client.Common.Models.Subsonic;
using Client.Common.Services;
using Client.Common.Services.DataStructures.SubsonicService;
using Subsonic8.AudioPlayback;
using Subsonic8.Framework.Interfaces;
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
            Preferences(); GainPreferences(); Lifecycle(); Compatibility();
            Console.WriteLine("PASS: " + _assertions + " production audio assertions with simulated native events. Decoding, XAML and background audio remain unverified.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
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
