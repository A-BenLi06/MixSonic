namespace Client.Tests.Mocks
{
    using System;

    using Mixdrome.Framework.Interfaces;

    internal class MockExtendedPlayerControls : MockPlayerControls, IExtendedPlayerControls
    {
        public bool CanSeek { get; set; }
        public double Volume { get; set; }
        public bool IsMuted { get; set; }
        public double PlaybackRate { get; set; }
        public void Seek(TimeSpan position) { }
        public TimeSpan GetCurrentPosition()
        {
            return TimeSpan.Zero;
        }

        public TimeSpan GetCurrentDuration()
        {
            return TimeSpan.Zero;
        }
    }
}
