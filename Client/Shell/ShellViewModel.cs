namespace Mixdrome.Shell
{
    using System;
    using System.Threading.Tasks;
    using BugFreak;
    using Caliburn.Micro;

    using Client.Common.Helpers;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using Client.Common.Services;
    using MugenInjection.Attributes;
    using BottomBar;
    using ErrorDialog;
    using Framework;
    using Framework.Extensions;
    using Framework.Interfaces;
    using Framework.Services;
    using Main;
    using Playback;
    using Search;
    using Settings;
    using Services;

    using Mixdrome.AudioPlayback;

    using VideoPlayback;
    using Windows.UI.Xaml;

    public class ShellViewModel : Screen, IShellViewModel
    {
        #region Fields

        private IEventAggregator _eventAggregator;

        private IBottomBarViewModel _bottomBar;
        private bool _isInitializing;


        #endregion

        #region Constructors and Destructors

        public ShellViewModel()
        {
            DisplayName = global::Mixdrome.MixRadioText.Get("ApplicationName");
        }

        #endregion

        #region Public Properties

        public IBottomBarViewModel BottomBar
        {
            get
            {
                return _bottomBar;
            }

            set
            {
                if (_bottomBar == value)
                {
                    return;
                }

                _bottomBar = value;
                NotifyOfPropertyChange();
                NotifyOfPropertyChange(() => HasBottomBar);
            }
        }

        public Action<SearchResultCollection> NavigateToSearchResult { get; set; }
        public bool HasBottomBar { get { return BottomBar != null; } }
        public bool IsInitializing
        {
            get { return _isInitializing; }
            private set { _isInitializing = value; NotifyOfPropertyChange(); }
        }


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

        [Inject]
        public IDialogNotificationService DialogNotificationService { get; set; }

        [Inject]
        public ICustomFrameAdapter NavigationService { get; set; }

        [Inject]
        public IToastNotificationService NotificationService { get; set; }

        [Inject]
        public IStorageService StorageService { get; set; }

        [Inject]
        public ISubsonicService SubsonicService { get; set; }

        [Inject]
        public IWinRTWrappersService WinRTWrappersService { get; set; }

        [Inject]
        public ICharmsBarService CharmsBarService { get; set; }

        [Inject]
        public IErrorDialogViewModel ErrorDialogViewModel { get; set; }

        [Inject]
        public IResourceService ResourceService { get; set; }

        [Inject]
        public ISettingsHelper SettingsHelper { get; set; }

        [Inject]
        public IoCService IoCService { get; set; }

        [Inject]
        public IPlayerManagementService PlayerManagementService { get; set; }

        [Inject]
        public IAudioPlayerViewModel AudioPlayerViewModel { get; set; }

        [Inject]
        public IEmbeddedVideoPlaybackViewModel EmbeddedVideoPlaybackViewModel { get; set; }

        [Inject]
        public IFullScreenVideoPlaybackViewModel FullScreenVideoPlaybackViewModel { get; set; }

        #endregion

        #region Public Methods and Operators

        public void Handle(ChangeBottomBarMessage message)
        {
            BottomBar = message.BottomBarViewModel;
        }

        public async Task HandleError(Exception error)
        {
            await new MessageDialogResult(error.ToString(), "Oops...").Execute();
        }

        public void SendSearchQueryMessage(string query)
        {
            NavigationService.NavigateToViewModel<Mixdrome.Navidrome.NavidromeBrowserViewModel>(Mixdrome.Navidrome.NavidromeRoute.For("search", query));
        }

        #endregion

        #region Methods

        protected async override void OnViewAttached(object view, object context)
        {
            App.LogStartupEvent("Shell view attached");
            base.OnViewAttached(view, context);
            IsInitializing = true;
            try
            {
                await InitializeViewDependentServices(view);
            }
            catch (Exception exception)
            {
                App.LogStartupException("Shell initialization failed", exception);
                await ErrorDialogViewModel.HandleCriticalError(exception);
            }
            finally { IsInitializing = false; }
#if NATIVE_UI_PROBE
            await Mixdrome.Diagnostics.NativeUiProbe.RunAsync((ShellView)view, NavigationService);
#endif
        }

        private async Task InitializeViewDependentServices(object view)
        {
            App.LogStartupEvent("Shell initialization start");
            // SearchBox owns the window's search integration. Initializing the legacy
            // SearchPane here prevents SearchBox from being constructed on this view.
            CharmsBarService.RegisterSettingsRequestedHandler(
                (sender, args) => args.AddSetting<SettingsViewModel>());
            CharmsBarService.RegisterSettingsRequestedHandler(
                (sender, args) => args.AddSetting<PrivacyPolicyViewModel>());
            WinRTWrappersService.RegisterMediaControlHandler(new MediaControlHandler(_eventAggregator));

            HookBugFreak();

            RegisterPlayers();

            InstantiateRequiredSingletons();

            App.LogStartupEvent("Settings load start");
            await LoadSettings();
            App.LogStartupEvent("Settings load complete");

            await RestoreLastViewOrGoToMain((ShellView) view);
            App.LogStartupEvent("Shell initialization complete");
        }

        private void RegisterPlayers()
        {
            PlayerManagementService.RegisterAudioPlayer(AudioPlayerViewModel);
            PlayerManagementService.RegisterVideoPlayer(EmbeddedVideoPlaybackViewModel);
            PlayerManagementService.RegisterVideoPlayer(FullScreenVideoPlaybackViewModel);
        }

        private void InstantiateRequiredSingletons()
        {
            IoCService.Get<IPlaybackViewModel>();
            IoCService.Get<INotificationsHelper>();
            IoCService.Get<Mixdrome.Navidrome.IPlaybackActivityService>();
        }

        private async Task LoadSettings()
        {
            await SettingsHelper.LoadSettings();
        }

        private async Task RestoreLastViewOrGoToMain(ShellView shellView)
        {
            App.LogStartupEvent("Session restore start");
            try
            {
                await SuspensionManager.RestoreAsync();
            }
            catch (SuspensionManagerException)
            {
            }

            SuspensionManager.RegisterFrame(shellView.ShellFrame, "MainFrame");
            App.LogStartupEvent("Session restore complete: " + shellView.ShellFrame.SourcePageType);

            if (shellView.ShellFrame.SourcePageType == null ||
                shellView.ShellFrame.SourcePageType == typeof(ErrorDialogView))
            {
                var navigated = NavigationExtensions.NavigateToViewModel<MainViewModel>(NavigationService);
                if (!navigated) throw new InvalidOperationException(Mixdrome.Navidrome.NavidromeText.Get("HomeNavigationFailed"));
                App.LogStartupEvent("Home navigation complete: " + shellView.ShellFrame.SourcePageType);
            }
        }

        private void HookBugFreak()
        {
            var apiKey = ResourceService.GetStringResource("BugFreakCredentials/ApiKey");
            var token = ResourceService.GetStringResource("BugFreakCredentials/Token");
            BugFreak.Hook(apiKey, token, Application.Current);
        }

        #endregion
    }
}
