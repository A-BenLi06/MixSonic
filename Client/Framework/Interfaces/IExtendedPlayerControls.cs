namespace Subsonic8.Framework.Interfaces
{
    using System;

    public interface IExtendedPlayerControls : IPlayerControls
    {
        TimeSpan GetCurrentPosition();

        TimeSpan GetCurrentDuration();

        bool CanSeek { get; }
        void Seek(TimeSpan position);
        double Volume { get; set; }
        bool IsMuted { get; set; }
        double PlaybackRate { get; set; }
    }
}
