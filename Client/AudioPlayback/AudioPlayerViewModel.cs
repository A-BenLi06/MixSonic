namespace Subsonic8.AudioPlayback
{
    using System;
    using System.Globalization;

    using Windows.UI.Xaml;

    using Caliburn.Micro;

    using Client.Common.EventAggregatorMessages;
    using Client.Common.Models;
    using Client.Common.Services;

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
        private bool _stopped = true;
        private bool _opened;
        private bool _playWhenOpened;
        private bool _retriedCompatibleStream;
        private bool _currentStreamIsMp3;
        private string _originalPlaybackError;

        public AudioPlayerViewModel()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            _volume = Math.Max(0, Math.Min(1, ReadNumber("AudioVolume", 1)));
            _rate = Math.Max(0.5, Math.Min(2, ReadNumber("AudioRate", 1)));
            object muted; bool value;
            if (settings.TryGetValue("AudioMuted", out muted) && bool.TryParse(Convert.ToString(muted, CultureInfo.InvariantCulture), out value)) _isMuted = value;
        }

        private static double ReadNumber(string key, double fallback)
        {
            object value; double number;
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out value) &&
                double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
                !double.IsNaN(number) && !double.IsInfinity(number) ? number : fallback;
        }

        #endregion

        #region Public Events

        public event EventHandler<EventArgs> PlaybackPaused;

        public event EventHandler<EventArgs> PlaybackStarted;

        public event EventHandler<EventArgs> PlaybackStoped;

        public event EventHandler PlaybackSeeked;
        public PlaylistItem CurrentItem { get { return _currentItem; } }
        public bool IsActuallyPlaying { get; private set; }
        public bool CanSeek { get { return _opened && !_stopped && _playerControls != null && _playerControls.CanSeek; } }
        public bool IsBuffering { get; private set; }
        public string PlaybackError { get; private set; }
        public bool IsUsingCompatibleStream { get; private set; }
        [Inject] public ISubsonicService SubsonicService { get; set; }
        public TimeSpan LastStoppedPosition { get; private set; }
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

        public void RestorePosition(long positionMilliseconds)
        {
            if (positionMilliseconds < 0 || _stopped) return;
            if (_opened) Seek(TimeSpan.FromMilliseconds(Math.Min(positionMilliseconds, (long)GetDuration().TotalMilliseconds)));
            else _startOptions = new PlaybackStartOptions { PositionMilliseconds = positionMilliseconds };
        }

        public void MediaOpened()
        {
            if (_stopped) return;
            _opened = true;
            NotifyOfPropertyChange(() => CanSeek);
            if (_startOptions != null && _startOptions.PositionMilliseconds > 0)
                Seek(TimeSpan.FromMilliseconds(Math.Min(_startOptions.PositionMilliseconds, Math.Max(0, (long)GetDuration().TotalMilliseconds))));
            _startOptions = null;
            if (_playWhenOpened) _playerControls.Play();
            else _playerControls.Pause();
        }

        public void Pause()
        {
            _playWhenOpened = false;
            if (_opened && _playerControls != null) _playerControls.Pause();
        }

        public void Play(PlaylistItem item, object options = null)
        {
            Stop();
            _currentItem = item;
            _retriedCompatibleStream = false; _originalPlaybackError = null; IsUsingCompatibleStream = false;
            _currentStreamIsMp3 = false;
            if (CanResolveStream(item))
            {
                item.Uri = SubsonicService.GetUriForFileWithId(item.SongId);
                _currentStreamIsMp3 = SubsonicService.Configuration.CompatibleMode ||
                    string.Equals(SubsonicService.Configuration.StreamingFormat, "mp3", StringComparison.OrdinalIgnoreCase);
            }
            NotifyOfPropertyChange(() => IsUsingCompatibleStream);
            OpenSource(item.Uri, options as PlaybackStartOptions);
            NotifyOfPropertyChange(() => CurrentItem);
        }

        private bool CanResolveStream(PlaylistItem item)
        {
            return item != null && !string.IsNullOrEmpty(item.SongId) && item.Uri != null &&
                !string.IsNullOrEmpty(item.AccountKey) &&
                (item.Uri.Scheme == "http" || item.Uri.Scheme == "https") && SubsonicService != null &&
                item.AccountKey == AccountIdentity.For(SubsonicService.Configuration);
        }

        private void OpenSource(Uri source, PlaybackStartOptions options)
        {
            _stopped = false;
            _opened = false; PlaybackError = null; IsBuffering = true;
            _playWhenOpened = true;
            NotifyOfPropertyChange(() => PlaybackError); NotifyOfPropertyChange(() => IsBuffering); NotifyOfPropertyChange(() => CanSeek);
            _reportedMediaStart = false;
            _startOptions = options;
            IsActuallyPlaying = false;
            Source = null;
            Source = source;
        }

        public void Resume()
        {
            _playWhenOpened = true;
            if (_opened && !_stopped && _playerControls != null) _playerControls.Play();
        }

        public void MediaStateChanged(object sender, RoutedEventArgs args)
        {
            var media = sender as Windows.UI.Xaml.Controls.MediaElement;
            if (media == null) return;
            IsActuallyPlaying = !_stopped && media.CurrentState == MediaElementState.Playing;
            IsBuffering = !_stopped && (media.CurrentState == MediaElementState.Buffering || media.CurrentState == MediaElementState.Opening);
            NotifyOfPropertyChange(() => IsActuallyPlaying);
            NotifyOfPropertyChange(() => IsBuffering);
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
            if (_stopped) return;
            FinishPlayback();
            EventAggregator.PublishOnUIThread(new PlayNextMessage());
        }

        public void SongFailed(ExceptionRoutedEventArgs eventArgs)
        {
            if (_stopped) return;
            if (!_reportedMediaStart && !_retriedCompatibleStream && !_currentStreamIsMp3 && CanResolveStream(_currentItem))
            {
                _retriedCompatibleStream = true; _originalPlaybackError = eventArgs.ErrorMessage;
                var options = _startOptions ?? new PlaybackStartOptions { PositionMilliseconds = Math.Max(0, (long)GetCurrentPosition().TotalMilliseconds) };
                FinishPlayback();
                _currentStreamIsMp3 = true; IsUsingCompatibleStream = true;
                NotifyOfPropertyChange(() => IsUsingCompatibleStream);
                OpenSource(SubsonicService.GetUriForFileWithId(_currentItem.SongId, true), options);
                return;
            }
            PlaybackError = eventArgs.ErrorMessage; NotifyOfPropertyChange(() => PlaybackError);
            if (!string.IsNullOrEmpty(_originalPlaybackError)) PlaybackError = _originalPlaybackError + Environment.NewLine + PlaybackError;
            FinishPlayback();
            NotifyOfPropertyChange(() => PlaybackError);
            EventAggregator.PublishOnUIThread(new PlayFailedMessage(PlaybackError, eventArgs.OriginalSource));
        }

        public void Stop()
        {
            FinishPlayback();
            if (_playerControls != null) _playerControls.Stop();
            Source = null;
        }

        private void FinishPlayback()
        {
            if (_stopped) return;
            _stopped = true;
            LastStoppedPosition = GetCurrentPosition();
            IsActuallyPlaying = false;
            IsBuffering = false;
            NotifyOfPropertyChange(() => IsActuallyPlaying);
            NotifyOfPropertyChange(() => IsBuffering); NotifyOfPropertyChange(() => CanSeek);
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
