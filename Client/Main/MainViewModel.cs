namespace Subsonic8.Main
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using MugenInjection.Attributes;
    using Subsonic8.Framework.Services;
    using Subsonic8.Framework.ViewModel;
    using Subsonic8.Index;
    using Subsonic8.MenuItem;
    using Subsonic8.Settings;

    public class MainViewModel : CollectionViewModelBase<bool, IList<MusicFolder>>, IMainViewModel
    {
        #region Constructors and Destructors

        public MainViewModel()
        {
            MenuItems = new BindableCollection<MenuItemViewModel>();
            UpdateDisplayName = () => DisplayName = "my music";
        }

        #endregion

        #region Public Properties

        [Inject]
        public IResourceService ResourceService { get; set; }

        [Inject]
        public IDialogService DialogService { get; set; }

        #endregion

        #region Public Methods and Operators

        public override async Task Populate()
        {
            ErrorDialogViewModel.Hide();
            if (SubsonicService.HasValidSubsonicUrl)
            {
                if (await ShouldPopulate())
                {
                    await base.Populate();
                }
            }
            else
            {
                await ShowSettingsNotFoundDialog();
                DialogService.ShowSettings<SettingsViewModel>();
            }
        }

        #endregion

        #region Methods

        protected override IEnumerable<IMediaModel> GetItemsToDisplay(IList<MusicFolder> result)
        {
            return new IMediaModel[]
                       {
                           new LibraryCommand(
                               "random",
                               "play me",
                               "random mix",
                               SubsonicModelTypeEnum.LibraryRandomSongs),
                           new LibraryCommand(
                               string.Empty,
                               "albums",
                               "album wall",
                               SubsonicModelTypeEnum.LibraryAlbums),
                           new LibraryCommand(
                               "artists",
                               "artists",
                               "album artists",
                               SubsonicModelTypeEnum.LibraryArtists),
                           new LibraryCommand(
                               "genres",
                               "genres",
                               "categories",
                               SubsonicModelTypeEnum.LibraryGenres),
                           new LibraryCommand(
                               "playlists",
                               "playlists",
                               "remote lists",
                               SubsonicModelTypeEnum.LibraryPlaylists),
                           new LibraryCommand(
                               "favourites",
                               "favourites",
                               "starred songs",
                               SubsonicModelTypeEnum.LibraryFavourites),
                           new LibraryCommand(
                               "radio",
                               "radio",
                               "internet stations",
                               SubsonicModelTypeEnum.LibraryRadio),
                           new LibraryCommand(
                               "now-playing",
                               "now playing",
                               "server activity",
                               SubsonicModelTypeEnum.LibraryNowPlaying)
                       };
        }

        protected override IServiceResultBase<IList<MusicFolder>> GetResult(bool parameter)
        {
            return SubsonicService.GetMusicFolders();
        }

        protected override Task AfterPopulate(bool parameter)
        {
            return base.AfterPopulate(parameter);
        }

        protected async override void OnActivate()
        {
            base.OnActivate();
            if (MenuItems.Count == 0)
            {
                await Populate();
            }
        }

        private async Task<bool> ShouldPopulate()
        {
            var populate = true;
            var diagnosticsResult = SubsonicService.Ping();
            await diagnosticsResult.Execute();
            if (diagnosticsResult.Error != null)
            {
                populate = false;
                ErrorDialogViewModel.HandleError(diagnosticsResult.Error);
            }

            return populate;
        }

        private async Task ShowSettingsNotFoundDialog()
        {
            var message = ResourceService.GetStringResource("ShellStrings/NotConfigured");
            await NotificationService.Show(new DialogNotificationOptions { Message = message });
        }

        #endregion
        public override bool IsGroupedView
        {
            get
            {
                return false;
            }
        }

    }
}
