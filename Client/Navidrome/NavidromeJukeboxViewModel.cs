namespace Subsonic8.Navidrome
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models.Navidrome;
    using Client.Common.Services;
    using Client.Common.Services.Navidrome;
    using MugenInjection.Attributes;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;

    public interface INavidromeJukeboxViewModel : IScreen { }
    public sealed class NavidromeJukeboxViewModel : NavidromePageViewModel, INavidromeJukeboxViewModel
    {
        private readonly DispatcherTimer _timer;
        private JukeboxState _state;
        private NavidromeItem _selectedTrack;
        private bool _polling;
        private string _loadedAccount;
        private double _gain;
        private bool _gainEdited;
        private int _requestVersion;
        private string _pollError;
        private sealed class Operation
        {
            public int PageVersion, RequestVersion, CurrentIndex;
            public string Account;
            public string[] Order;
        }
        public NavidromeJukeboxViewModel()
        {
            Tracks = new BindableCollection<NavidromeItem>(); UpdateDisplayName = () => { };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += async (sender, args) => { if (Ready && !_polling) await Poll(); };
        }
        [Inject] public IPlaylistManagementService PlaylistQueue { get; set; }
        public BindableCollection<NavidromeItem> Tracks { get; private set; }
        public NavidromeItem SelectedTrack { get { return _selectedTrack; } set { _selectedTrack = value; NotifyOfPropertyChange(); Changed(); } }
        private bool Ready { get { return !IsLoading && !IsWorking && _state != null && _loadedAccount == Session.AccountKey; } }
        public bool CanStart { get { return Ready && _state.Songs.Count > 0 && !_state.Playing; } }
        public bool CanStop { get { return Ready && _state.Playing; } }
        public bool CanPrevious { get { return CanSeek && _state.CurrentIndex > 0; } }
        public bool CanNext { get { return CanSeek && _state.CurrentIndex < _state.Songs.Count - 1; } }
        public bool CanRemove { get { return Ready && SelectedTrack != null && Tracks.Contains(SelectedTrack); } }
        public bool CanClear { get { return Ready && _state.Songs.Count > 0; } }
        public bool CanShuffle { get { return Ready && _state.Songs.Count > 1; } }
        public bool CanSeek { get { return Ready && _state.CurrentIndex >= 0 && _state.CurrentIndex < _state.Songs.Count; } }
        public bool CanSetGain { get { return Ready; } }
        public bool CanUseQueue { get { return Ready && PlaylistQueue.HasElements && PlaylistQueue.Items.All(item => item.AccountKey == Session.AccountKey && !string.IsNullOrEmpty(item.SongId)); } }
        public bool CanAppendQueue { get { return CanUseQueue; } }
        public bool IsEmpty { get { return Ready && _state.Songs.Count == 0; } }
        public string PlaybackStatus { get { return _state == null ? null : NavidromeText.Get(_state.Playing ? "ServerPlayerPlaying" : "ServerPlayerStopped"); } }
        public string Position { get { return _state == null ? null : NavidromeItem.FormatTime(_state.Position); } }
        public string CurrentSong { get { return _state == null || _state.CurrentIndex < 0 || _state.CurrentIndex >= _state.Songs.Count ? null : NavidromeItem.Join(_state.Songs[_state.CurrentIndex].Title, _state.Songs[_state.CurrentIndex].Artist); } }
        public double Gain
        {
            get { return _gain; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return;
                var gain = Math.Max(0, Math.Min(1, value));
                if (_gain == gain) return;
                _gain = gain; _gainEdited = true; NotifyOfPropertyChange();
            }
        }
        protected override void OnActivate() { PlaylistQueue.PropertyChanged += QueueChanged; _timer.Start(); base.OnActivate(); }
        protected override void OnDeactivate(bool close) { _timer.Stop(); ++_requestVersion; PlaylistQueue.PropertyChanged -= QueueChanged; base.OnDeactivate(close); }
        private void QueueChanged(object sender, PropertyChangedEventArgs args) { Changed(); }
        protected override void Changed()
        {
            base.Changed();
            foreach (var name in new[] { "CanStart", "CanStop", "CanPrevious", "CanNext", "CanRemove", "CanClear", "CanShuffle", "CanSeek", "CanSetGain", "CanUseQueue", "CanAppendQueue", "IsEmpty", "PlaybackStatus", "Position", "CurrentSong" }) NotifyOfPropertyChange(name);
        }
        public override async Task Reload()
        {
            var version = ++LoadVersion; var request = ++_requestVersion;
            IsLoading = true; Error = null; _pollError = null; _state = null; _loadedAccount = null;
            SelectedTrack = null; Tracks.Clear(); DisplayName = NavidromeText.Get("ServerPlayer");
            try
            {
                await Session.ConnectAsync(); var api = Session.CreateApi();
                if (Session.User == null || !Session.User.JukeboxRole) throw new InvalidOperationException(NavidromeText.Get("JukeboxUnavailable"));
                var state = await api.Jukebox(JukeboxAction.Get).ReadAsync();
                if (IsCurrent(version, api) && request == _requestVersion) { _loadedAccount = api.AccountKey; Apply(state, api, true); }
            }
            catch (Exception exception) { if (PageActive && version == LoadVersion && request == _requestVersion) Error = exception.Message; }
            finally { if (version == LoadVersion) { IsLoading = false; Changed(); } }
        }
        private void Apply(JukeboxState state, NavidromeApi api, bool resetGain)
        {
            var unchanged = _state != null && state.Songs.Select(song => song.Id).SequenceEqual(_state.Songs.Select(song => song.Id));
            _state = state;
            if (!unchanged) { SelectedTrack = null; Tracks.Clear(); Tracks.AddRange(state.Songs.Select((song, index) => { var item = NavidromeItem.From(song, api); item.Ordinal = index; return item; })); }
            if (resetGain || !_gainEdited) { _gain = state.Gain; _gainEdited = false; NotifyOfPropertyChange(() => Gain); }
            Changed();
        }
        private async Task Poll()
        {
            _polling = true; var version = LoadVersion; var request = ++_requestVersion; var account = _loadedAccount;
            try
            {
                var api = Session.CreateApi(); var state = await api.Jukebox(JukeboxAction.Get).ReadAsync();
                if (IsCurrent(version, api) && request == _requestVersion && account == api.AccountKey)
                {
                    Apply(state, api, false);
                    if (_pollError != null && Error == _pollError) Error = null;
                    _pollError = null;
                }
            }
            catch (Exception exception)
            {
                if (PageActive && version == LoadVersion && request == _requestVersion && account == Session.AccountKey)
                { _pollError = exception.Message; Error = _pollError; }
            }
            finally { _polling = false; }
        }
        private Operation BeginOperation()
        {
            return new Operation { PageVersion = LoadVersion, RequestVersion = ++_requestVersion, Account = _loadedAccount,
                CurrentIndex = _state.CurrentIndex, Order = _state.Songs.Select(song => song.Id).ToArray() };
        }
        private void RequireOperation(Operation operation, NavidromeApi api)
        {
            if (!IsCurrent(operation.PageVersion, api) || operation.RequestVersion != _requestVersion || operation.Account != api.AccountKey)
                throw new OperationCanceledException();
        }
        private async Task VerifyQueue(Operation operation, NavidromeApi api, bool currentTrack)
        {
            var latest = await api.Jukebox(JukeboxAction.Get).ReadAsync(); RequireOperation(operation, api);
            if (!operation.Order.SequenceEqual(latest.Songs.Select(song => song.Id)) || (currentTrack && operation.CurrentIndex != latest.CurrentIndex))
            { Apply(latest, api, false); throw new InvalidOperationException(NavidromeText.Get("ServerQueueChanged")); }
        }
        private async Task Refresh(Operation operation, NavidromeApi api, bool resetGain = false)
        {
            var state = await api.Jukebox(JukeboxAction.Get).ReadAsync(); RequireOperation(operation, api); Apply(state, api, resetGain);
        }
        private Task Command(JukeboxAction action, int? index = null, int? offset = null, double? gain = null, bool currentTrack = false, string confirmation = null)
        {
            if (!Ready) return Task.FromResult(false);
            var operation = BeginOperation();
            return RunOperation(async api =>
            {
                RequireOperation(operation, api);
                if (confirmation != null && !await Dialogs.ConfirmAsync(confirmation, "Clear")) return;
                RequireOperation(operation, api);
                if (index.HasValue || confirmation != null) await VerifyQueue(operation, api, currentTrack);
                RequireOperation(operation, api);
                await api.Jukebox(action, index: index, offset: offset, gain: gain).ReadAsync(); RequireOperation(operation, api);
                await Refresh(operation, api, action == JukeboxAction.SetGain);
            });
        }
        public Task Start() { return CanStart ? Command(JukeboxAction.Start) : Task.FromResult(false); }
        public Task Stop() { return CanStop ? Command(JukeboxAction.Stop) : Task.FromResult(false); }
        public Task Previous() { return CanPrevious ? Command(JukeboxAction.Skip, _state.CurrentIndex - 1, 0, currentTrack: true) : Task.FromResult(false); }
        public Task Next() { return CanNext ? Command(JukeboxAction.Skip, _state.CurrentIndex + 1, 0, currentTrack: true) : Task.FromResult(false); }
        public Task Remove() { return CanRemove ? Command(JukeboxAction.Remove, SelectedTrack.Ordinal) : Task.FromResult(false); }
        public Task Shuffle() { return CanShuffle ? Command(JukeboxAction.Shuffle) : Task.FromResult(false); }
        public Task SetGain() { return CanSetGain ? Command(JukeboxAction.SetGain, gain: Gain) : Task.FromResult(false); }
        public Task PlayTrack(ItemClickEventArgs args)
        {
            var item = args.ClickedItem as NavidromeItem;
            return Ready && item != null && Tracks.Contains(item) ? Command(JukeboxAction.Skip, item.Ordinal, 0) : Task.FromResult(false);
        }
        public Task UseQueue() { return QueueCommand(false); }
        public Task AppendQueue() { return QueueCommand(true); }
        private Task QueueCommand(bool append)
        {
            if (!CanUseQueue) return Task.FromResult(false);
            var queue = PlaylistQueue.Items.ToList(); var operation = BeginOperation();
            return RunOperation(async api =>
            {
                RequireOperation(operation, api);
                if (queue.Count == 0 || queue.Any(item => item.AccountKey != api.AccountKey || string.IsNullOrEmpty(item.SongId))) throw new InvalidOperationException(NavidromeText.Get("QueueContainsOtherMedia"));
                if (!append && !await Dialogs.ConfirmAsync("ConfirmReplaceServerQueue", "Replace")) return;
                RequireOperation(operation, api);
                if (!append) await VerifyQueue(operation, api, false);
                RequireOperation(operation, api); await api.Jukebox(append ? JukeboxAction.Add : JukeboxAction.Set, queue.Select(item => item.SongId)).ReadAsync();
                RequireOperation(operation, api); await Refresh(operation, api);
            });
        }
        public Task Clear()
        { return CanClear ? Command(JukeboxAction.Clear, confirmation: "ConfirmClearServerQueue") : Task.FromResult(false); }
        public Task Seek(FrameworkElement anchor)
        {
            if (!CanSeek) return Task.FromResult(false);
            var operation = BeginOperation(); var duration = _state.Songs[operation.CurrentIndex].Duration;
            var field = new FormField { LabelKey = "PositionSeconds", Value = _state.Position.ToString(CultureInfo.CurrentCulture) };
            return RunOperation(async api =>
            {
                RequireOperation(operation, api); int seconds;
                if (!await Dialogs.FormAsync(anchor, "Seek", new[] { field }, () =>
                    int.TryParse(field.Value as string, NumberStyles.Integer, CultureInfo.CurrentCulture, out seconds) && seconds >= 0 && (duration <= 0 || seconds <= duration)
                        ? null : NavidromeText.Get("InvalidPosition"))) return;
                RequireOperation(operation, api); await VerifyQueue(operation, api, true);
                RequireOperation(operation, api);
                await api.Jukebox(JukeboxAction.Skip, index: operation.CurrentIndex, offset: int.Parse((string)field.Value, CultureInfo.CurrentCulture)).ReadAsync();
                RequireOperation(operation, api); await Refresh(operation, api);
            });
        }
    }
}
