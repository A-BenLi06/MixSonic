namespace Mixdrome.Framework.ViewModel
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.EventAggregatorMessages;
    using Client.Common.Models;
    using Client.Common.Results;
    using MugenInjection.Attributes;
    using Mixdrome.BottomBar;
    using Mixdrome.Framework.Extensions;
    using Mixdrome.Framework.Services;
    using Mixdrome.MenuItem;
    using Windows.UI.Xaml.Controls;
    using Mixdrome.BrowsePlayer;

    public abstract class CollectionViewModelBase<TParameter, TResult> : ViewModelBase, ICollectionViewModel<TParameter>
    {
        #region Fields

        private IBottomBarViewModel _bottomBar;

        [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1305:FieldNamesMustNotUseHungarianNotation",
            Justification = "Reviewed. Suppression is OK here.")]
        private IIoCService _ioCService;

        private bool _hasItems;

        private bool _isEmpty;

        private bool _loadedOnce;

        private BindableCollection<MenuItemViewModel> _menuItems;

        private TParameter _parameter;

        private bool _isLoading;
        private int _populateVersion;

        #endregion

        #region Constructors and Destructors

        protected CollectionViewModelBase()
        {
            LoadPlaylistItem = this.LoadPlaylistItemFromSong;
            MenuItems = new BindableCollection<MenuItemViewModel>();
        }

        #endregion

        #region Public Properties

        [Inject] public IBrowsePlayerViewModel PlayerSummary { get; set; }

        public virtual bool ShowRecentListening { get { return false; } }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set
            {
                _isLoading = value;
                NotifyOfPropertyChange(() => IsLoading);
                UpdateEmptyState();
            }
        }

        public IBottomBarViewModel BottomBar
        {
            get
            {
                return _bottomBar;
            }

            set
            {
                _bottomBar = value;
                NotifyOfPropertyChange();
            }
        }

        public virtual string EmptyStateSubtitle
        {
            get
            {
                return MixRadioText.Get("CollectionEmptySubtitle");
            }
        }

        public virtual string EmptyStateTitle
        {
            get
            {
                return MixRadioText.Get("CollectionEmptyTitle");
            }
        }

        public bool HasItems
        {
            get
            {
                return _hasItems;
            }

            private set
            {
                if (value.Equals(_hasItems))
                {
                    return;
                }

                _hasItems = value;
                NotifyOfPropertyChange();
            }
        }

        public bool IsEmpty
        {
            get
            {
                return _isEmpty;
            }

            private set
            {
                if (value.Equals(_isEmpty))
                {
                    return;
                }

                _isEmpty = value;
                NotifyOfPropertyChange();
            }
        }

        public BindableCollection<MenuItemViewModel> MenuItems
        {
            get
            {
                return _menuItems;
            }

            set
            {
                if (Equals(value, _menuItems))
                {
                    return;
                }

                _menuItems = value;
                NotifyOfPropertyChange(() => MenuItems);
                NotifyOfPropertyChange(() => GroupedMenuItems);
                UpdateEmptyState();
            }
        }

        /// <summary>
        /// Menu items grouped by the first character of their title so browse pages can
        /// render MixRadio-style letter headers (see FullScreen.xaml group style).
        /// </summary>
        public virtual IEnumerable<IGrouping<string, MenuItemViewModel>> GroupedMenuItems
        {
            get
            {
                return (from item in MenuItems
                        let title = item.Title ?? string.Empty
                        let key = title.Length > 0 ? char.ToUpper(title[0]).ToString() : "#"
                        group item by key into g
                        select g).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            }
        }

        public virtual bool IsGroupedView
        {
            get
            {
                return true;
            }
        }

        public bool IsFlatView
        {
            get
            {
                return !IsGroupedView;
            }
        }

        public bool ShowGroupedItems
        {
            get
            {
                return HasItems && IsGroupedView;
            }
        }

        public bool ShowFlatItems
        {
            get
            {
                return HasItems && IsFlatView;
            }
        }

        public TParameter Parameter
        {
            get
            {
                return _parameter;
            }

            set
            {
                _parameter = value;
                NotifyOfPropertyChange();
                AwaitPopulate();
            }
        }

        public ObservableCollection<object> SelectedItems
        {
            get
            {
                return BottomBar != null ? BottomBar.SelectedItems : new ObservableCollection<object>();
            }
        }

        public Func<IId, Task<PlaylistItem>> LoadPlaylistItem { get; set; }

        [Inject]
        public IIoCService IoCService
        {
            get
            {
                return _ioCService;
            }

            set
            {
                _ioCService = value;
                LoadBottomBar();
            }
        }

        #endregion

        #region Public Methods and Operators

        public async virtual Task ChildClick(ItemClickEventArgs eventArgs)
        {
            await HandleItemSelection(((MenuItemViewModel)eventArgs.ClickedItem).Item);
        }

        public async Task HandleItemSelection(ISubsonicModel subsonicModel)
        {
            if (subsonicModel.Type == SubsonicModelTypeEnum.Song || subsonicModel.Type == SubsonicModelTypeEnum.Video)
            {
                // Get all songs/videos from current view
                var allItems = MenuItems
                    .Select(m => m.Item)
                    .Where(item => item.Type == SubsonicModelTypeEnum.Song || item.Type == SubsonicModelTypeEnum.Video)
                    .ToList();

                if (allItems.Count > 0)
                {
                    // Find the index of the clicked item
                    var clickedIndex = allItems.IndexOf(subsonicModel);
                    if (clickedIndex < 0) clickedIndex = 0;

                    // Load all playlist items
                    var playlistItems = new List<PlaylistItem>();
                    foreach (var item in allItems)
                    {
                        var playlistItem = await LoadPlaylistItem(item);
                        if (playlistItem == null) return;
                        playlistItems.Add(playlistItem);
                    }

                    var playQueueMessage = new PlayQueueMessage
                    {
                        Queue = playlistItems,
                        StartIndex = clickedIndex
                    };
                    EventAggregator.PublishOnUIThread(playQueueMessage);
                }
            }

            NavigationService.NavigateByModelType(subsonicModel);
        }

        public virtual async Task Populate()
        {
            var version = ++_populateVersion;
            var parameter = Parameter;
            IsLoading = true;
            try
            {
                if (PlayerSummary != null) await PlayerSummary.LoadRecentAsync();
                await GetResult(parameter).WithErrorHandler(ErrorDialogViewModel)
                    .OnSuccess(result => { if (version == _populateVersion) OnResultSuccessfull(result); }).Execute();
                if (version == _populateVersion)
                {
                    await AfterPopulate(parameter);
                    UpdateDisplayName();
                }
            }
            finally { if (version == _populateVersion) IsLoading = false; }
        }

        #endregion

        #region Methods

        protected virtual Task AfterPopulate(TParameter parameter)
        {
            return Task.Factory.StartNew(() => { });
        }

        protected abstract IEnumerable<IMediaModel> GetItemsToDisplay(TResult result);

        protected abstract IServiceResultBase<TResult> GetResult(TParameter parameter);

        protected virtual void LoadBottomBar()
        {
            BottomBar = IoCService.Get<IDefaultBottomBarViewModel>();
        }

        protected override void OnActivate()
        {
            base.OnActivate();
            SetAppBottomBar();
        }

        protected virtual void OnResultSuccessfull(TResult result)
        {
            PopulateMenuItems(result);
        }

        private void PopulateMenuItems(TResult result)
        {
            MenuItems.Clear();
            var children = GetItemsToDisplay(result) ?? Enumerable.Empty<IMediaModel>();
            MenuItems.AddRange(children.Select(s => s.AsMenuItemViewModel()));
            NotifyOfPropertyChange(() => GroupedMenuItems);
            _loadedOnce = true;
            UpdateEmptyState();
        }

        private void SetAppBottomBar()
        {
            EventAggregator.PublishOnUIThread(new ChangeBottomBarMessage { BottomBarViewModel = BottomBar });
        }

        private void UpdateEmptyState()
        {
            var hasItems = MenuItems != null && MenuItems.Count > 0;
            HasItems = hasItems;
            IsEmpty = _loadedOnce && !_isLoading && !hasItems;
            NotifyOfPropertyChange(() => ShowGroupedItems);
            NotifyOfPropertyChange(() => ShowFlatItems);
        }

        private async void AwaitPopulate()
        {
            await Populate();
        }

        #endregion
    }
}
