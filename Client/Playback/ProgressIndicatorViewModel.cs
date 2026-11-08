namespace Subsonic8.Playback
{
    using System;
    using Windows.UI.Xaml;
    using Caliburn.Micro;
    using MugenInjection.Attributes;
    using Subsonic8.AudioPlayback;

    public class ProgressIndicatorViewModel : Screen, IProgressIndicatorViewModel
    {
        private IAudioPlayerViewModel _audioPlayerViewModel;
        private double _itemDurationInSeconds;
        private double _playbackProgressInSeconds;
        private double _progressStepFrequency;
        private DispatcherTimer _progressTimer;

        #region Fields

        #endregion

        #region Public Properties

        [Inject]
        public IAudioPlayerViewModel AudioPlayerViewModel
        {
            get { return _audioPlayerViewModel; }
            set
            {
                if (_audioPlayerViewModel != null)
                {
                    _audioPlayerViewModel.PlaybackPaused -= AudioPlayerViewModelOnPlaybackPaused;
                    _audioPlayerViewModel.PlaybackStarted -= AudioPlayerViewModelOnPlaybackStarted;
                    _audioPlayerViewModel.PlaybackStoped -= AudioPlayerViewModelOnPlaybackStoped;
                }
                _audioPlayerViewModel = value;
                HookAudioPlayer();
            }
        }

        public double ItemDurationInSeconds
        {
            get { return _itemDurationInSeconds; }
            set
            {
                if (value.Equals(_itemDurationInSeconds)) return;
                _itemDurationInSeconds = value;
                NotifyOfPropertyChange();
                NotifyOfPropertyChange(() => DurationText);
            }
        }

        public double PlaybackProgressInSeconds
        {
            get { return _playbackProgressInSeconds; }
            set
            {
                if (value.Equals(_playbackProgressInSeconds)) return;
                _playbackProgressInSeconds = value;
                NotifyOfPropertyChange();
                NotifyOfPropertyChange(() => PositionText);
            }
        }

        public string PositionText { get { return FormatTime(PlaybackProgressInSeconds); } }
        public string DurationText { get { return FormatTime(ItemDurationInSeconds); } }

        private static string FormatTime(double seconds)
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        }

        public double ProgressStepFrequency
        {
            get { return _progressStepFrequency; }
            set
            {
                if (value.Equals(_progressStepFrequency)) return;
                _progressStepFrequency = value;
                NotifyOfPropertyChange();
            }
        }

        #endregion

        #region Methods

        private void OnProgressTimerTick(object sender, object e)
        {
            UpdateCurrentItemProgress();
        }

        protected void HookAudioPlayer()
        {
            AudioPlayerViewModel.PlaybackPaused += AudioPlayerViewModelOnPlaybackPaused;
            AudioPlayerViewModel.PlaybackStarted += AudioPlayerViewModelOnPlaybackStarted;
            AudioPlayerViewModel.PlaybackStoped += AudioPlayerViewModelOnPlaybackStoped;
        }

        private void AudioPlayerViewModelOnPlaybackStarted(object sender, EventArgs eventArgs)
        {
            ItemDurationInSeconds = AudioPlayerViewModel.GetDuration().TotalSeconds;
            UpdateCurrentItemProgress();
            StartProgressTimer();
        }

        private void AudioPlayerViewModelOnPlaybackStoped(object sender, EventArgs eventArgs)
        {
            StopProgressTimer();
            ItemDurationInSeconds = 0;
            PlaybackProgressInSeconds = 0;
        }

        private void AudioPlayerViewModelOnPlaybackPaused(object sender, EventArgs eventArgs)
        {
            StopProgressTimer();
        }

        private void StartProgressTimer()
        {
            if (_progressTimer == null)
            {
                _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _progressTimer.Tick += OnProgressTimerTick;
            }
            _progressTimer.Start();
        }

        private void StopProgressTimer()
        {
            if (_progressTimer != null)
            {
                _progressTimer.Stop();
            }
        }

        private void UpdateCurrentItemProgress()
        {
            PlaybackProgressInSeconds = AudioPlayerViewModel.GetCurrentPosition().TotalSeconds;
        }

        #endregion
    }
}
