namespace Subsonic8.Navidrome
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models.Navidrome;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.Navidrome;

    public enum OfflineDownloadState { Queued, Downloading, Completed, Failed, Cancelled, Missing }

    public sealed class OfflineDownloadItem : PropertyChangedBase
    {
        private OfflineDownloadState _state;
        private string _error, _stage;
        private long _received;
        private long? _total;
        internal CancellationTokenSource Cancellation;
        internal TaskCompletionSource<bool> Finished;
        internal NavidromeApi Api;
        internal bool StructuredLyrics;
        public string AccountKey { get; internal set; }
        public Song Song { get; internal set; }
        public DownloadedSong Record { get; internal set; }
        public bool OriginalAvailable { get; internal set; }
        public bool PlaybackAvailable { get; internal set; }
        public string ImageUrl { get; internal set; }
        public string Title { get { return Song.Title; } }
        public string Subtitle { get { return NavidromeItem.Join(Song.Artist, Song.Album, NavidromeItem.FormatTime(Song.Duration)); } }
        public string Error { get { return _error; } internal set { _error = value; NotifyOfPropertyChange(); NotifyOfPropertyChange(() => HasError); } }
        public bool HasError { get { return !string.IsNullOrEmpty(Error); } }
        public OfflineDownloadState State { get { return _state; } internal set { _state = value; Changed(); } }
        public string StateText { get { return NavidromeText.Get("Download" + State); } }
        public string Stage { get { return _stage; } internal set { _stage = value; NotifyOfPropertyChange(); } }
        public bool IsTransferring { get { return State == OfflineDownloadState.Downloading; } }
        public bool CanCancel { get { return State == OfflineDownloadState.Queued || State == OfflineDownloadState.Downloading; } }
        public bool CanRetry { get { return State == OfflineDownloadState.Failed || State == OfflineDownloadState.Cancelled || State == OfflineDownloadState.Missing || (State == OfflineDownloadState.Completed && !PlaybackAvailable); } }
        public bool CanPlay { get { return PlaybackAvailable && Record != null; } }
        public bool IsIndeterminate { get { return !_total.HasValue || _total.Value <= 0; } }
        public double Progress { get { return IsIndeterminate ? 0 : Math.Min(100, _received * 100.0 / _total.Value); } }
        public string ProgressText { get { return string.Format(CultureInfo.CurrentCulture, NavidromeText.Get("DownloadBytes"), _received); } }
        internal void Report(DownloadProgress progress)
        {
            _received = progress.Received; _total = progress.Total;
            NotifyOfPropertyChange(() => Progress); NotifyOfPropertyChange(() => IsIndeterminate); NotifyOfPropertyChange(() => ProgressText);
        }
        internal void Changed()
        {
            foreach (var name in new[] { "State", "StateText", "CanCancel", "CanRetry", "CanPlay", "OriginalAvailable", "PlaybackAvailable", "ImageUrl", "IsTransferring", "Title", "Subtitle" }) NotifyOfPropertyChange(name);
        }
    }
}
