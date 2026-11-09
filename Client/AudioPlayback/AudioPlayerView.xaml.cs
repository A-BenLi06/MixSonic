namespace Subsonic8.AudioPlayback
{
    using System;
    using System.Threading.Tasks;

    using Windows.UI.Core;

    using Subsonic8.Framework.Interfaces;

    public sealed partial class AudioPlayerView : IExtendedPlayerControls
    {
        #region Constructors and Destructors

        public AudioPlayerView()
        {
            InitializeComponent();
        }

        #endregion

        #region Public Methods and Operators

        public async void Pause()
        {
            await RunOnDispatcher(() => MediaElement.Pause());
        }

        public async void Play()
        {
            await RunOnDispatcher(() => MediaElement.Play());
        }

        public async void Stop()
        {
            await RunOnDispatcher(() => MediaElement.Stop());
        }

        public TimeSpan GetCurrentPosition()
        {
            return MediaElement.Position;
        }

        public TimeSpan GetCurrentDuration()
        {
            return MediaElement.NaturalDuration.HasTimeSpan ? MediaElement.NaturalDuration.TimeSpan : TimeSpan.Zero;
        }

        public bool CanSeek { get { return MediaElement.CanSeek; } }
        public void Seek(TimeSpan position) { MediaElement.Position = position; }
        public double Volume { get { return MediaElement.Volume; } set { MediaElement.Volume = value; } }
        public bool IsMuted { get { return MediaElement.IsMuted; } set { MediaElement.IsMuted = value; } }
        public double PlaybackRate
        {
            get { return MediaElement.PlaybackRate; }
            set { MediaElement.DefaultPlaybackRate = value; MediaElement.PlaybackRate = value; }
        }

        #endregion

        #region Methods

        private async Task RunOnDispatcher(Action action)
        {
            if (Dispatcher.HasThreadAccess) action();
            else await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action());
        }

        #endregion
    }
}
