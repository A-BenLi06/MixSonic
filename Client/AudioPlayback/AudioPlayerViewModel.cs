namespace Mixdrome.AudioPlayback
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;

    using Windows.UI.Xaml;

    using Caliburn.Micro;

    using Client.Common.EventAggregatorMessages;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services;

    using MugenInjection.Attributes;

    using Mixdrome.Framework.Interfaces;
    using Mixdrome.Navidrome;
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
        private ReplayGainMode _gainMode;
        private double _preAmplification;
        private bool _stopped = true;
        private bool _opened;
        private bool _playWhenOpened;
        private bool _retriedCompatibleStream;
        private bool _currentStreamIsMp3;
        private string _originalPlaybackError;
        private CancellationTokenSource _streamCancellation;
        private Task _streamResolution;
        private int _openVersion;
        private bool _resolvingStream;

        public AudioPlayerViewModel()
        {
            var settings = ApplicationData.Current.LocalSettings.Values;
            _volume = Math.Max(0, Math.Min(1, ReadNumber("AudioVolume", 1)));
            _rate = Math.Max(0.5, Math.Min(2, ReadNumber("AudioRate", 1)));
            _preAmplification = Math.Max(-15, Math.Min(15, ReadNumber("AudioPreAmplification", 0)));
            var savedGain = ReadNumber("AudioReplayGainMode", 0);
            if (savedGain >= 0 && savedGain <= int.MaxValue && savedGain == Math.Truncate(savedGain) && Enum.IsDefined(typeof(ReplayGainMode), (int)savedGain)) _gainMode = (ReplayGainMode)(int)savedGain;
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
        [Inject] public INavidromeStreamService StreamService { get; set; }
        public string StreamInformation { get; private set; }
        public TimeSpan LastStoppedPosition { get; private set; }
        public double Volume
        {
            get { return _volume; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return;
                _volume = Math.Max(0, Math.Min(1, value));
                ApplyVolume();
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

        public ReplayGainMode GainMode
        {
            get { return _gainMode; }
            set
            {
                if (!Enum.IsDefined(typeof(ReplayGainMode), value)) return;
                _gainMode = value;
                ApplicationData.Current.LocalSettings.Values["AudioReplayGainMode"] = (int)value;
                NotifyOfPropertyChange(); ApplyVolume();
            }
        }
        public double PreAmplification
        {
            get { return _preAmplification; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < -15 || value > 15) return;
                _preAmplification = value;
                ApplicationData.Current.LocalSettings.Values["AudioPreAmplification"] = value;
                NotifyOfPropertyChange(); ApplyVolume();
            }
        }
        public double RequestedGain { get { return ReplayGain.Calculate(_currentItem == null ? null : _currentItem.ReplayGain, _gainMode, _preAmplification); } }
        public double EffectiveVolume { get { return Math.Max(0, Math.Min(1, _volume * RequestedGain)); } }
        public bool IsGainLimited { get { return _volume * RequestedGain > 1; } }
        private void ApplyVolume()
        {
            if (_playerControls != null) _playerControls.Volume = EffectiveVolume;
            NotifyOfPropertyChange(() => EffectiveVolume); NotifyOfPropertyChange(() => IsGainLimited);
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
                if (value != null) { ApplyVolume(); value.IsMuted = _isMuted; value.PlaybackRate = _rate; }
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
            if (_stopped || _resolvingStream) return;
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
            ApplyVolume();
            _retriedCompatibleStream = false; _originalPlaybackError = null; IsUsingCompatibleStream = false;
            _currentStreamIsMp3 = false;
            StreamInformation = null; NotifyOfPropertyChange(() => StreamInformation);
            if (StreamService != null && CanResolveStream(item))
            {
                _stopped = false; _opened = false; _reportedMediaStart = false; _resolvingStream = true; _playWhenOpened = true;
                _startOptions = options as PlaybackStartOptions; PlaybackError = null; IsActuallyPlaying = false; IsBuffering = true;
                NotifyOfPropertyChange(() => IsActuallyPlaying); NotifyOfPropertyChange(() => IsBuffering); NotifyOfPropertyChange(() => PlaybackError); NotifyOfPropertyChange(() => CanSeek);
                _streamCancellation = new CancellationTokenSource();
                _streamResolution = ResolveStreamAsync(item, _openVersion, _streamCancellation.Token);
                NotifyOfPropertyChange(() => CurrentItem); NotifyOfPropertyChange(() => IsUsingCompatibleStream);
                return;
            }
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

        private async Task ResolveStreamAsync(PlaylistItem item, int version, CancellationToken cancellation)
        {
            var configuration = SubsonicService.Configuration;
            var secret = configuration.Password;
            var authentication = configuration.AuthenticationMode;
            try
            {
                var stream = await StreamService.ResolveAsync(item, cancellation);
                if (cancellation.IsCancellationRequested || version != _openVersion || !ReferenceEquals(item, _currentItem)) return;
                var currentConfiguration = SubsonicService.Configuration;
                if (!CanResolveStream(item) || currentConfiguration == null || currentConfiguration.Password != secret || currentConfiguration.AuthenticationMode != authentication)
                { FailResolution(NavidromeText.Get("PlaybackConnectionChanged")); return; }
                if (stream == null || stream.Uri == null) throw new InvalidOperationException(NavidromeText.Get("InvalidStreamDecision"));
                _resolvingStream = false; _currentStreamIsMp3 = stream.IsMp3;
                if (stream.Negotiated) StreamInformation = string.Format(CultureInfo.CurrentCulture, NavidromeText.Get(stream.Transcoded ? "TranscodedStream" : "DirectStream"), stream.Container);
                NotifyOfPropertyChange(() => StreamInformation);
                OpenSource(stream.Uri, _startOptions, _playWhenOpened);
            }
            catch (OperationCanceledException)
            { if (version == _openVersion && ReferenceEquals(item, _currentItem)) FailResolution(NavidromeText.Get("PlaybackConnectionChanged")); }
            catch (Exception exception)
            { if (version == _openVersion && ReferenceEquals(item, _currentItem)) FailResolution(exception.Message); }
        }

        private void FailResolution(string message)
        {
            _resolvingStream = false; PlaybackError = message; FinishPlayback();
            NotifyOfPropertyChange(() => PlaybackError);
            EventAggregator.PublishOnUIThread(new PlayFailedMessage(message, null));
        }

        private void OpenSource(Uri source, PlaybackStartOptions options, bool playWhenOpened = true)
        {
            _stopped = false;
            _opened = false; PlaybackError = null; IsBuffering = true;
            _playWhenOpened = playWhenOpened;
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
            if (_resolvingStream) return;
            var media = sender as Windows.UI.Xaml.Controls.MediaElement;
            if (media == null) return;
            IsActuallyPlaying = !_stopped && _opened && media.CurrentState == MediaElementState.Playing;
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
            if (_stopped || _resolvingStream) return;
            FinishPlayback();
            EventAggregator.PublishOnUIThread(new PlayNextMessage());
        }

        public void SongFailed(ExceptionRoutedEventArgs eventArgs)
        {
            if (_stopped || _resolvingStream) return;
            if (!_reportedMediaStart && !_retriedCompatibleStream && !_currentStreamIsMp3 && CanResolveStream(_currentItem))
            {
                _retriedCompatibleStream = true; _originalPlaybackError = eventArgs.ErrorMessage;
                var options = _startOptions ?? new PlaybackStartOptions { PositionMilliseconds = Math.Max(0, (long)GetCurrentPosition().TotalMilliseconds) };
                FinishPlayback();
                _currentStreamIsMp3 = true; IsUsingCompatibleStream = true;
                StreamInformation = null; NotifyOfPropertyChange(() => StreamInformation);
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
            ++_openVersion;
            if (_streamCancellation != null) { _streamCancellation.Cancel(); _streamCancellation.Dispose(); _streamCancellation = null; }
            _resolvingStream = false;
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
