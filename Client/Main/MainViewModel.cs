namespace Subsonic8.Main
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using MugenInjection.Attributes;
    using Subsonic8.Framework.Services;
    using Subsonic8.Framework.ViewModel;
    using Subsonic8.Index;
    using Subsonic8.Navidrome;
    using Subsonic8.Framework.Extensions;
    using Subsonic8.MenuItem;
    using Subsonic8.Settings;

    public class MainViewModel : CollectionViewModelBase<bool, IList<MusicFolder>>, IMainViewModel
    {
        #region Constructors and Destructors

        public MainViewModel()
        {
            MenuItems = new BindableCollection<MenuItemViewModel>();
            UpdateDisplayName = () => DisplayName = MixRadioText.Get("MyMusic");
        }

        #endregion

        #region Public Properties

        public override bool ShowRecentListening { get { return true; } }

        [Inject]
        public IResourceService ResourceService { get; set; }

        [Inject]
        public IDialogService DialogService { get; set; }

        #endregion

        #region Public Methods and Operators

        public override async Task Populate()
        {
            ErrorDialogViewModel.Hide();
            var menu = new BindableCollection<MenuItemViewModel>();
            menu.AddRange(GetItemsToDisplay(null).Select(item => item.AsMenuItemViewModel()));
            MenuItems = menu;
            if (PlayerSummary != null) await PlayerSummary.LoadRecentAsync();
            if (!SubsonicService.HasValidSubsonicUrl)
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
                               MixRadioText.Get("PlayMe"),
                               MixRadioText.Get("RandomMix"),
                               SubsonicModelTypeEnum.LibraryRandomSongs),
                           new LibraryCommand(
                               string.Empty,
                               MixRadioText.Get("Albums"),
                               MixRadioText.Get("AlbumWall"),
                               SubsonicModelTypeEnum.LibraryAlbums),
                           new LibraryCommand(
                               "artists",
                               MixRadioText.Get("Artists"),
                               MixRadioText.Get("AlbumArtists"),
                               SubsonicModelTypeEnum.LibraryArtists),
                           new LibraryCommand(
                               "genres",
                               MixRadioText.Get("Genres"),
                               MixRadioText.Get("Categories"),
                               SubsonicModelTypeEnum.LibraryGenres),
                           new LibraryCommand(
                               "playlists",
                               MixRadioText.Get("Playlists"),
                               MixRadioText.Get("RemoteLists"),
                               SubsonicModelTypeEnum.LibraryPlaylists),
                           new LibraryCommand(
                               "favourites",
                               MixRadioText.Get("Favourites"),
                               MixRadioText.Get("StarredSongs"),
                               SubsonicModelTypeEnum.LibraryFavourites),
                           new LibraryCommand(
                               "radio",
                               MixRadioText.Get("Radio"),
                               MixRadioText.Get("InternetStations"),
                               SubsonicModelTypeEnum.LibraryRadio),
                           new LibraryCommand(
                               "now-playing",
                               MixRadioText.Get("NowPlaying"),
                               MixRadioText.Get("ServerActivity"),
                               SubsonicModelTypeEnum.LibraryNowPlaying),
                           new LibraryCommand("songs", NavidromeText.Get("Sectionsongs"), NavidromeText.Get("AllSongs"), SubsonicModelTypeEnum.LibrarySongs),
                           new LibraryCommand("bookmarks", NavidromeText.Get("Sectionbookmarks"), NavidromeText.Get("SavedPositions"), SubsonicModelTypeEnum.LibraryBookmarks),
                           new LibraryCommand("shares", NavidromeText.Get("Sectionshares"), NavidromeText.Get("SharedLinks"), SubsonicModelTypeEnum.LibraryShares)
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
        public override bool IsGroupedView        {            get            {                return false;            }        }
    }
}
