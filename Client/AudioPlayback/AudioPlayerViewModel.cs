namespace Subsonic8.AudioPlayback
{
    using System;

    using Windows.UI.Xaml;

    using Caliburn.Micro;

    using Client.Common.EventAggregatorMessages;
    using Client.Common.Models;

    using MugenInjection.Attributes;

    using Subsonic8.Framework.Interfaces;
    using Windows.Storage;
    using Windows.UI.Xaml.Media;

    public class AudioPlayerViewModel : Screen, IAudioPlayerViewModel
    {
        #region Fields

        private IEventAggregator _eventAggregator;

        private IExtendedPlayerControls _playerControls;

        private Uri _source;

        private PlaylistItem _currentItem;

        private bool _reportedMediaStart;

        private PlaybackStartOptions _startOptions;
        private double _volume = 1;
        private bool _isMuted;
        private double _rate = 1;

        public AudioPlayerViewModel()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            if (settings.ContainsKey("AudioVolume")) _volume = Math.Max(0, Math.Min(1, Convert.ToDouble(settings["AudioVolume"])));
            if (settings.ContainsKey("AudioMuted")) _isMuted = Convert.ToBoolean(settings["AudioMuted"]);
            if (settings.ContainsKey("AudioRate")) _rate = Math.Max(0.5, Math.Min(2, Convert.ToDouble(settings["AudioRate"])));
        }

        #endregion

        #region Public Events

        public event EventHandler<EventArgs> PlaybackPaused;

        public event EventHandler<EventArgs> PlaybackStarted;

        public event EventHandler<EventArgs> PlaybackStoped;

        public event EventHandler PlaybackSeeked;
        public PlaylistItem CurrentItem { get { return _currentItem; } }
        public bool IsActuallyPlaying { get; private set; }
        public bool CanSeek { get { return _playerControls != null && _playerControls.CanSeek; } }
        public double Volume
        {
            get { return _volume; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return;
                _volume = Math.Max(0, Math.Min(1, value));
                if (_playerControls != null) _playerControls.Volume = _volume;
                ApplicationData.Current.LocalSettings.Values["AudioVolume"] = _volume;
                NotifyOfPropertyChange(() => Volume);
            }
        }
        public bool IsMuted
        {
            get { return _isMuted; }
            set
            {
                _isMuted = value;
                if (_playerControls != null) _playerControls.IsMuted = value;
                ApplicationData.Current.LocalSettings.Values["AudioMuted"] = value;
                NotifyOfPropertyChange(() => IsMuted);
            }
        }
        public double PlaybackRate
        {
            get { return _rate; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.5 || value > 2) return;
                _rate = value;
                if (_playerControls != null) _playerControls.PlaybackRate = value;
                ApplicationData.Current.LocalSettings.Values["AudioRate"] = value;
                NotifyOfPropertyChange(() => PlaybackRate);
            }
        }

        #endregion

        #region Public Properties

        [Inject]
        public IEventAggregator EventAggregator
        {
            get
            {
                return _eventAggregator;
            }

            set
            {
                _eventAggregator = value;
                _eventAggregator.Subscribe(this);
            }
        }

        public IExtendedPlayerControls PlayerControls
        {
            get
            {
                return _playerControls;
            }

            set
            {
                _playerControls = value;
                if (value != null) { value.Volume = _volume; value.IsMuted = _isMuted; value.PlaybackRate = _rate; }
                NotifyOfPropertyChange();
            }
        }

        public Uri Source
        {
            get
            {
                return _source;
            }

            set
            {
                _source = value;
                NotifyOfPropertyChange();
            }
        }

        #endregion

        #region Public Methods and Operators

        public TimeSpan GetCurrentPosition()
        {
            return _playerControls == null ? TimeSpan.Zero : _playerControls.GetCurrentPosition();
        }

        public TimeSpan GetDuration()
        {
            var duration = _playerControls == null ? TimeSpan.Zero : _playerControls.GetCurrentDuration();
            return duration > TimeSpan.Zero ? duration : _currentItem == null ? TimeSpan.Zero : TimeSpan.FromSeconds(_currentItem.Duration);
        }

        public void Seek(TimeSpan position)
        {
            if (!CanSeek) return;
            var duration = GetDuration();
            position = position < TimeSpan.Zero ? TimeSpan.Zero : position > duration ? duration : position;
            _playerControls.Seek(position);
            if (PlaybackSeeked != null) PlaybackSeeked(this, EventArgs.Empty);
        }

        public void MediaOpened()
        {
            NotifyOfPropertyChange(() => CanSeek);
            if (_startOptions != null && _startOptions.PositionMilliseconds > 0)
                Seek(TimeSpan.FromMilliseconds(_startOptions.PositionMilliseconds));
            _startOptions = null;
            _playerControls.Play();
        }

        public void Pause()
        {
            if (_playerControls != null) _playerControls.Pause();
        }

        public void Play(PlaylistItem item, object options = null)
        {
            _currentItem = item;
            _reportedMediaStart = false;
            _startOptions = options as PlaybackStartOptions;
            IsActuallyPlaying = false;
            Source = null;
            Source = item.Uri;
            NotifyOfPropertyChange(() => CurrentItem);
        }

        public void Resume()
        {
            if (_playerControls != null) _playerControls.Play();
        }

        public void MediaStateChanged(object sender, RoutedEventArgs args)
        {
            var media = sender as Windows.UI.Xaml.Controls.MediaElement;
            if (media == null) return;
            IsActuallyPlaying = media.CurrentState == MediaElementState.Playing;
            NotifyOfPropertyChange(() => IsActuallyPlaying);
            NotifyOfPropertyChange(() => CanSeek);
            if (media.CurrentState == MediaElementState.Paused && PlaybackPaused != null) PlaybackPaused(this, EventArgs.Empty);
            if (!IsActuallyPlaying) return;
            if (PlaybackStarted != null) PlaybackStarted(this, EventArgs.Empty);
            if (!_reportedMediaStart && _currentItem != null)
            {
                _reportedMediaStart = true;
                EventAggregator.PublishOnUIThread(new TrackPlaybackStartedMessage(_currentItem));
            }
        }

        public void SongEnded()
        {
            IsActuallyPlaying = false;
            if (PlaybackStoped != null)
            {
                PlaybackStoped(this, new EventArgs());
            }
            EventAggregator.PublishOnUIThread(new PlayNextMessage());
        }

        public void SongFailed(ExceptionRoutedEventArgs eventArgs)
        {
            IsActuallyPlaying = false;
            if (PlaybackStoped != null)
            {
                PlaybackStoped(this, new EventArgs());
            }
            EventAggregator.PublishOnUIThread(new PlayFailedMessage(eventArgs.ErrorMessage, eventArgs.OriginalSource));
        }

        public void Stop()
        {
            if (_playerControls != null) _playerControls.Stop();
            IsActuallyPlaying = false;
            if (PlaybackStoped != null)
            {
                PlaybackStoped(this, new EventArgs());
            }
        }

        #endregion

        #region Methods

        protected override void OnViewAttached(object view, object context)
        {
            base.OnViewAttached(view, context);
            PlayerControls = (IExtendedPlayerControls)view;
        }



        #endregion
    }
}
