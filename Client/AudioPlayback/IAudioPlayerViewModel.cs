namespace Subsonic8.AudioPlayback
{
    using Caliburn.Micro;
    using System;
    using Client.Common.Models;

    using Client.Common.Services.DataStructures.PlayerManagementService;

    public interface IAudioPlayerViewModel : IPlayer, IScreen, IPlaybackReporter
    {
        event EventHandler PlaybackSeeked;
        PlaylistItem CurrentItem { get; }
        bool IsActuallyPlaying { get; }
        bool CanSeek { get; }
        bool IsBuffering { get; }
        string PlaybackError { get; }
        bool IsUsingCompatibleStream { get; }
        string StreamInformation { get; }
        TimeSpan LastStoppedPosition { get; }
        double Volume { get; set; }
        bool IsMuted { get; set; }
        double PlaybackRate { get; set; }
        Client.Common.Models.Subsonic.ReplayGainMode GainMode { get; set; }
        double PreAmplification { get; set; }
        double EffectiveVolume { get; }
        bool IsGainLimited { get; }
        void Seek(TimeSpan position);
        void RestorePosition(long positionMilliseconds);
    }
}
