namespace Client.Common.Services
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Xml.Serialization;
    using Caliburn.Micro;
    using Client.Common.EventAggregatorMessages;
    using Client.Common.Models;
    using Client.Common.Services.DataStructures.PlaylistManagementService;
    using Action = System.Action;

    public class PlaylistManagementService : PropertyChangedBase, IPlaylistManagementService
    {
        #region Constants

        public const int NoTrackTrackNumber = -1;

        #endregion

        #region Fields

        private readonly IEventAggregator _eventAggregator;

        private readonly Random _randomNumberGenerator;

        private bool _isPaused;

        private bool _isPlaying;

        private PlaylistItemCollection _items;

        private bool _shuffleOn;

        private bool _wasEmpty;

        private PlaybackRepeatMode _repeatMode;

        private PlaylistItem _currentItem;

        private readonly List<PlaylistItem> _shuffleRemaining = new List<PlaylistItem>();

        private bool _shuffleCycleStarted;

        private bool _isGoingBack;

        #endregion

        #region Constructors and Destructors

        public PlaylistManagementService(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);

            StartPlaybackAction = StartPlayback;
            StopPlaybackAction = StopPlayback;
            GetNextTrackNumberFunc = GetNextTrackNumber;
            GetPreviousTrackNumberFunc = GetPreviousTrackNumber;

            Items = new PlaylistItemCollection();
            PlaylistHistory = new PlaylistHistoryStack();
            _randomNumberGenerator = new Random();

            CurrentItem = null;
            _wasEmpty = true;
        }

        #endregion

        #region Public Properties

        public PlaylistItem CurrentItem
        {
            get { return _currentItem; }
            private set
            {
                if (ReferenceEquals(value, _currentItem)) return;
                _currentItem = value;
                NotifyOfPropertyChange(() => CurrentItem);
                NotifyOfPropertyChange(() => CurrentTrackNumber);
            }
        }

        public int CurrentTrackNumber
        {
            get
            {
                return Items != null && CurrentItem != null && Items.Contains(CurrentItem)
                           ? Items.IndexOf(CurrentItem)
                           : NoTrackTrackNumber;
            }
        }

        public Func<int> GetNextTrackNumberFunc { get; set; }

        public Func<int> GetPreviousTrackNumberFunc { get; set; }

        public bool HasElements
        {
            get
            {
                return Items.Any();
            }
        }

        public bool IsPaused
        {
            get
            {
                return _isPaused;
            }

            private set
            {
                _isPaused = value;
                if (_isPaused)
                {
                    IsPlaying = false;
                }

                NotifyOfPropertyChange(() => IsPaused);
            }
        }

        public bool IsPlaying
        {
            get
            {
                return _isPlaying;
            }

            private set
            {
                if (value.Equals(_isPlaying))
                {
                    return;
                }

                _isPlaying = value;
                if (_isPlaying)
                {
                    IsPaused = false;
                }

                NotifyOfPropertyChange();
            }
        }

        public PlaylistItemCollection Items
        {
            get
            {
                return _items;
            }

            private set
            {
                _items = value;
                if (_items != null)
                {
                    Items.CollectionChanged += PlaylistChanged;
                }
            }
        }

        public PlaylistHistoryStack PlaylistHistory { get; private set; }

        public bool ShuffleOn
        {
            get
            {
                return _shuffleOn;
            }

            private set
            {
                if (value.Equals(_shuffleOn))
                {
                    return;
                }

                _shuffleOn = value;
                ResetShuffleCycle();
                NotifyOfPropertyChange();
            }
        }

        public bool RepeatOn
        {
            get { return RepeatMode != PlaybackRepeatMode.Off; }
        }

        public PlaybackRepeatMode RepeatMode
        {
            get { return _repeatMode; }
            private set
            {
                if (value == _repeatMode) return;
                _repeatMode = value;
                NotifyOfPropertyChange(() => RepeatMode);
                NotifyOfPropertyChange(() => RepeatOn);
            }
        }

        public Action<int> StartPlaybackAction { get; set; }

        public Action StopPlaybackAction { get; set; }

        #endregion

        #region Public Methods and Operators

        public void Clear()
        {
            Items.Clear();
        }

        public int GetNextTrackNumber()
        {
            int result;
            if (Items.Count == 0)
            {
                result = NoTrackTrackNumber;
            }
            else if (ShuffleOn)
            {
                result = GetNextShuffleTrackNumber();
            }
            else if (CurrentTrackNumber == (Items.Count - 1))
            {
                result = RepeatMode == PlaybackRepeatMode.All ? 0 : NoTrackTrackNumber;
            }
            else
            {
                result = CurrentTrackNumber + 1;
            }

            return result;
        }

        public int GetPreviousTrackNumber()
        {
            return ShuffleOn ? PlaylistHistory.Count == 0 ? NoTrackTrackNumber : PlaylistHistory.Pop() : (CurrentTrackNumber - 1);
        }

        public void Handle(JumpToNextMessage message)
        {
            PlayOrStop(GetNextTrackNumberFunc());
        }

        public void Handle(JumpToPreviousMessage message)
        {
            var previousTrackNumber = GetPreviousTrackNumberFunc();
            if (previousTrackNumber > NoTrackTrackNumber)
            {
                _isGoingBack = true;
                try { StartPlaybackAction(previousTrackNumber); }
                finally { _isGoingBack = false; }
            }
        }

        public void Handle(PlayQueueMessage message)
        {
            if (message.Queue == null || message.Queue.Count == 0 ||
                message.StartIndex < 0 || message.StartIndex >= message.Queue.Count) return;

            var queue = message.Queue.ToList();
            StopPlaybackAction();
            Items.Clear();
            Items.AddRange(queue);
            if (message.Options == null) StartPlaybackAction(message.StartIndex);
            else StartPlaybackWithOptions(message.StartIndex, message.Options);
        }

        public void InsertNext(IEnumerable<PlaylistItem> items)
        {
            var queue = items.ToList();
            if (queue.Any(item => item == null)) throw new ArgumentException("Queue entries cannot be null.", "items");
            var index = CurrentTrackNumber + 1;
            foreach (var item in queue) Items.Insert(index++, item);
        }

        public void MoveItem(PlaylistItem item, int index)
        {
            var source = Items.IndexOf(item);
            if (source < 0 || index < 0 || index >= Items.Count) throw new ArgumentOutOfRangeException("index");
            Items.Move(source, index);
            NotifyOfPropertyChange(() => CurrentTrackNumber);
        }

        public void Handle(AddItemsMessage message)
        {
            if (message.Queue.Count == 0) return;
            Items.AddRange(message.Queue);
            if (message.StartPlaying)
            {
                StartPlaybackAction(Items.Count - message.Queue.Count);
            }
        }

        public void Handle(RemoveItemsMessage message)
        {
            if (message.Queue == null) return;
            var removed = message.Queue.ToList();
            var removedCurrent = removed.Contains(CurrentItem);
            var currentIndex = CurrentTrackNumber;
            var wasPlaying = IsPlaying;
            foreach (var item in removed)
            {
                Items.Remove(item);
            }
            if (removedCurrent && wasPlaying && Items.Count > 0)
            {
                StartPlaybackAction(Math.Min(currentIndex, Items.Count - 1));
            }
        }

        public void Handle(ToggleShuffleMessage message)
        {
            ShuffleOn = !ShuffleOn;
        }

        public void Handle(PlayPauseMessage message)
        {
            PlayPause();
        }

        public void Handle(PlayItemAtIndexMessage message)
        {
            var index = message.Index;
            if (index >= 0 && index < Items.Count)
            {
                StartPlaybackAction(index);
            }
        }

        public void Handle(StopMessage message)
        {
            StopPlaybackAction();
        }

        public void Handle(PauseMessage message)
        {
            Pause();
        }

        public void Handle(PlayMessage message)
        {
            Play(message.Options);
        }

        public void Handle(ToggleRepeatMessage message)
        {
            RepeatMode = RepeatMode == PlaybackRepeatMode.Off ? PlaybackRepeatMode.All :
                         RepeatMode == PlaybackRepeatMode.All ? PlaybackRepeatMode.One :
                         PlaybackRepeatMode.Off;
        }

        public void Handle(PlayNextMessage message)
        {
            int result;
            if (Items.Count == 0)
            {
                result = NoTrackTrackNumber;
            }
            else if (RepeatMode == PlaybackRepeatMode.One)
            {
                result = CurrentTrackNumber == NoTrackTrackNumber ? 0 : CurrentTrackNumber;
            }
            else
            {
                result = GetNextTrackNumberFunc();
            }

            PlayOrStop(result);
        }

        public void LoadPlaylist(PlaylistItemCollection playlistItemCollection)
        {
            if (playlistItemCollection == null)
            {
                return;
            }

            var imported = playlistItemCollection.ToList();
            if (imported.Any(item => item == null || item.Uri == null || !item.Uri.IsAbsoluteUri))
            {
                throw new InvalidDataException("A playlist entry has no absolute media URI.");
            }

            StopPlaybackAction();
            Items.Clear();
            Items.AddRange(imported);
        }

        public string GetStateAsString()
        {
            string result;
            var playlistServiceState = new PlaylistServiceState
                                           {
                                               CurrentTrackNumber = CurrentTrackNumber,
                                               IsShuffleOn = ShuffleOn,
                                               Items = Items,
                                               IsPlaying = IsPlaying,
                                               RepeatMode = RepeatMode
                                           };
            var serializer = GetStateSerializer();
            using (var memoryStream = new MemoryStream())
            {
                serializer.WriteObject(memoryStream, playlistServiceState);
                memoryStream.Flush();
                result = Convert.ToBase64String(memoryStream.ToArray());
            }

            return result;
        }

        public void SetStateFromString(string stateString)
        {
            var bytes = Convert.FromBase64String(stateString);
            PlaylistServiceState state;
            using (var memoryStream = new MemoryStream(bytes))
            {
                try
                {
                    var serializer = GetStateSerializer();
                    state = (PlaylistServiceState)serializer.ReadObject(memoryStream);
                }
                catch (Exception)
                {
                    memoryStream.Position = 0;
                    var serializer = GetLegacyStateSerializer();
                    state = (PlaylistServiceState)serializer.Deserialize(memoryStream);
                }
            }

            StopPlaybackAction();
            Items.Clear();
            if (state.Items != null) Items.AddRange(state.Items);
            CurrentItem = state.CurrentTrackNumber >= 0 && state.CurrentTrackNumber < Items.Count
                              ? Items[state.CurrentTrackNumber] : null;
            ShuffleOn = state.IsShuffleOn;
            RepeatMode = Enum.IsDefined(typeof(PlaybackRepeatMode), state.RepeatMode)
                             ? state.RepeatMode : PlaybackRepeatMode.Off;
            if (state.IsPlaying && CurrentItem != null)
            {
                Play();
            }
        }

        public void Pause()
        {
            if (!IsPlaying)
            {
                return;
            }

            _eventAggregator.PublishOnUIThreadAsync(new PausePlaybackMessage(CurrentItem));
            IsPaused = true;
        }

        public void Play(object options = null)
        {
            if (CurrentItem != null)
            {
                if (IsPaused)
                {
                    Resume();
                }
                else
                {
                    StartCurrentItemPlayback(options);
                }
            }
            else
            {
                StartPlaybackAction(GetNextTrackNumberFunc());
            }
        }

        public void PlayPause()
        {
            if (IsPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        public void Resume()
        {
            _eventAggregator.PublishOnUIThreadAsync(new ResumePlaybackMessage(CurrentItem));
            IsPlaying = true;
        }

        public void StartPlayback(int trackNumber)
        {
            StartPlaybackWithOptions(trackNumber, null);
        }

        private void StartPlaybackWithOptions(int trackNumber, object options)
        {
            if (trackNumber < 0 || trackNumber >= Items.Count)
            {
                return;
            }
            
            var previousTrackNumber = CurrentTrackNumber;
            if (!_isGoingBack && previousTrackNumber != NoTrackTrackNumber && previousTrackNumber != trackNumber)
            {
                PlaylistHistory.Push(previousTrackNumber);
            }

            StopPlaybackAction();
            CurrentItem = Items.ElementAt(trackNumber);
            _shuffleRemaining.Remove(CurrentItem);
            StartCurrentItemPlayback(options);
        }

        public void StopPlayback()
        {
            if (CurrentItem == null)
            {
                return;
            }

            _eventAggregator.PublishOnUIThreadAsync(new StopPlaybackMessage(CurrentItem));
            CurrentItem.PlayingState = PlaylistItemState.NotPlaying;
            IsPlaying = false;
            IsPaused = false;
        }

        #endregion

        #region Methods

        private static XmlObjectSerializer GetStateSerializer()
        {
            return new DataContractSerializer(typeof(PlaylistServiceState), new[] { typeof(PlaylistItemCollection) });
        }

        private static XmlSerializer GetLegacyStateSerializer()
        {
            return new XmlSerializer(typeof(PlaylistServiceState), new[] { typeof(PlaylistItemCollection) });
        }

        private void PlaylistChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            PlaylistHistory.Clear();
            ResetShuffleCycle();
            var totalElements = Items.Count;

            var becameNotEmpty = totalElements > 0 && _wasEmpty;
            var becameEmpty = totalElements == 0 && !_wasEmpty;

            if (CurrentItem != null && !Items.Contains(CurrentItem))
            {
                StopPlayback();
                CurrentItem = null;
            }
            NotifyOfPropertyChange(() => HasElements);
            NotifyOfPropertyChange(() => CurrentTrackNumber);

            if (becameNotEmpty || becameEmpty)
            {
                _eventAggregator.PublishOnUIThreadAsync(new PlaylistStateChangedMessage(Items.Any()));
                _wasEmpty = !Items.Any();
            }
        }

        private void StartCurrentItemPlayback(object options = null)
        {
            _eventAggregator.PublishOnUIThreadAsync(new StartPlaybackMessage(CurrentItem) { Options = options });
            CurrentItem.PlayingState = PlaylistItemState.Playing;
            IsPlaying = true;
        }

        private void PlayOrStop(int trackNumber)
        {
            if (trackNumber == NoTrackTrackNumber) StopPlaybackAction();
            else StartPlaybackAction(trackNumber);
        }

        private void ResetShuffleCycle()
        {
            _shuffleRemaining.Clear();
            _shuffleCycleStarted = false;
        }

        private int GetNextShuffleTrackNumber()
        {
            if (!_shuffleCycleStarted)
            {
                _shuffleRemaining.AddRange(Items.Where(item => !ReferenceEquals(item, CurrentItem)));
                _shuffleCycleStarted = true;
            }
            if (_shuffleRemaining.Count == 0)
            {
                if (RepeatMode != PlaybackRepeatMode.All) return NoTrackTrackNumber;
                _shuffleRemaining.AddRange(Items);
            }
            var candidates = _shuffleRemaining.Where(item => _shuffleRemaining.Count == 1 ||
                                                        !ReferenceEquals(item, CurrentItem)).ToList();
            var next = candidates[_randomNumberGenerator.Next(candidates.Count)];
            _shuffleRemaining.Remove(next);
            return Items.IndexOf(next);
        }

        #endregion
    }
}
