namespace Mixdrome.VideoPlayback
{
    using System;
    using Mixdrome.Framework.Interfaces;

    public interface IVideoPlayerView : IPlayerControls
    {
        #region Public Methods

        void SetEndTime(TimeSpan endTime);

        void SetStartTime(TimeSpan startTime);

        TimeSpan GetEndTime();

        TimeSpan GetStartTime();

        TimeSpan GetTimeRemaining();

        #endregion
    }
}