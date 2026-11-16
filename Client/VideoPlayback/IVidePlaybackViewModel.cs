namespace Mixdrome.VideoPlayback
{
    using System;
    using Client.Common.Services.DataStructures.PlayerManagementService;
    using Mixdrome.Framework.Interfaces;
    using Mixdrome.Framework.ViewModel;
    using Windows.UI.Xaml;

    // ReSharper disable PossibleInterfaceMemberAmbiguity
    public interface IVidePlaybackViewModel : IPlaybackControlsViewModel, IViewModel, IToastNotificationCapable, IVideoPlayer
    {
        // ReSharper restore PossibleInterfaceMemberAmbiguity
        #region Public Events

        event EventHandler<PlaybackStateEventArgs> FullScreenChanged;

        #endregion

        #region Public Properties

        TimeSpan EndTime { get; set; }

        TimeSpan StartTime { get; set; }

        #endregion

        #region Public Methods and Operators

        void OnFullScreenChanged();

        void SongFailed(ExceptionRoutedEventArgs eventArgs);

        #endregion

        void ClearSource();
    }
}