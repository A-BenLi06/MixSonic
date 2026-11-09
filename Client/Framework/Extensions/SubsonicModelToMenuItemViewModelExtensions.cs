namespace Subsonic8.Framework.Extensions
{
    using Client.Common.Models;
    using Subsonic8.MenuItem;
    using Windows.UI;
    using Windows.UI.Xaml.Media;

    public static class SubsonicModelToMenuItemViewModelExtensions
    {
        #region Constants

        private const double CommandTileSize = 210;

        private const double MediaTileHeight = 170;

        private const double MediaTileWidth = 170;

        private const double WideTileWidth = 428;

        #endregion

        #region Public Methods and Operators

        public static MenuItemViewModel AsMenuItemViewModel<T>(this T item) where T : IMediaModel
        {
            var description = item.GetDescription();
            var viewModel = new MenuItemViewModel
                                {
                                    Title = description.Item1,
                                    CoverArtId = item.CoverArt,
                                    Type = string.Format("{0}(s)", item.Type.ToString()),
                                    Subtitle = description.Item2,
                                    Item = item,
                                    AccentBrush = MixRadioPink(),
                                    CaptionBrush = MixRadioPink(),
                                    TileWidth = MediaTileWidth,
                                    TileHeight = MediaTileHeight
                                };

            ApplyMixRadioVisuals(viewModel, item);
            return viewModel;
        }

        #endregion

        #region Methods

        private static void ApplyMixRadioVisuals(MenuItemViewModel viewModel, IMediaModel item)
        {
            switch (item.Type)
            {
                case SubsonicModelTypeEnum.LibraryRandomSongs:
                    MakeCommandTile(viewModel, MixRadioPink(), "\uE102");
                    viewModel.TileWidth = CommandTileSize;
                    viewModel.TileHeight = CommandTileSize;
                    break;
                case SubsonicModelTypeEnum.LibraryAlbums:
                    MakeLibraryTile(viewModel);
                    viewModel.TileWidth = WideTileWidth;
                    viewModel.TileHeight = CommandTileSize;
                    break;
                case SubsonicModelTypeEnum.LibraryArtists:
                    MakeLibraryTile(viewModel);
                    viewModel.TileWidth = CommandTileSize;
                    viewModel.TileHeight = CommandTileSize;
                    break;
                case SubsonicModelTypeEnum.LibraryGenres:
                    MakeLibraryTile(viewModel);
                    viewModel.TileWidth = CommandTileSize;
                    viewModel.TileHeight = CommandTileSize;
                    break;
                case SubsonicModelTypeEnum.LibraryPlaylists:
                case SubsonicModelTypeEnum.LibrarySongs:
                case SubsonicModelTypeEnum.LibraryBookmarks:
                case SubsonicModelTypeEnum.LibraryShares:
                case SubsonicModelTypeEnum.Playlist:
                    MakeLibraryTile(viewModel);
                    viewModel.TileWidth = CommandTileSize;
                    viewModel.TileHeight = CommandTileSize;
                    break;
                case SubsonicModelTypeEnum.LibraryFavourites:
                    MakeLibraryTile(viewModel);
                    viewModel.TileWidth = WideTileWidth;
                    viewModel.TileHeight = CommandTileSize;
                    break;
                case SubsonicModelTypeEnum.LibraryRadio:
                    MakeLibraryTile(viewModel);
                    viewModel.TileWidth = WideTileWidth;
                    viewModel.TileHeight = CommandTileSize;
                    break;
                case SubsonicModelTypeEnum.LibraryNowPlaying:
                    MakeCommandTile(viewModel, MixRadioPink(), "\uE102");
                    viewModel.TileWidth = CommandTileSize;
                    viewModel.TileHeight = CommandTileSize;
                    break;
                case SubsonicModelTypeEnum.InternetRadioStation:
                    MakeCommandTile(viewModel, MixRadioTeal(), "\uE189");
                    viewModel.TileWidth = WideTileWidth;
                    viewModel.TileHeight = CommandTileSize;
                    break;
                case SubsonicModelTypeEnum.Genre:
                    MakeCommandTile(viewModel, MixRadioYellow(), "\uE189");
                    viewModel.TileWidth = CommandTileSize;
                    viewModel.TileHeight = CommandTileSize;
                    break;
                case SubsonicModelTypeEnum.Artist:
                    viewModel.AccentBrush = MixRadioPink();
                    viewModel.CaptionBrush = MixRadioPink();
                    viewModel.CoverArtFallback = "ms-appx:///Assets/ArtistPlaceholder.png";
                    break;
                case SubsonicModelTypeEnum.Album:
                    viewModel.AccentBrush = MixRadioPink();
                    viewModel.CaptionBrush = MixRadioPink();
                    break;
                case SubsonicModelTypeEnum.Song:
                case SubsonicModelTypeEnum.Video:
                    viewModel.AccentBrush = MixRadioPink();
                    viewModel.CaptionBrush = MixRadioDarkCaption();
                    break;
            }
        }

        private static SolidColorBrush BrushFromArgb(byte alpha, byte red, byte green, byte blue)
        {
            return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        }

        private static void MakeCommandTile(MenuItemViewModel viewModel, Brush accentBrush, string glyph)
        {
            viewModel.AccentBrush = accentBrush;
            viewModel.CaptionBrush = accentBrush;
            viewModel.Glyph = glyph;
            viewModel.ImageOpacity = 0;
            viewModel.IsCommandTile = true;
        }

        private static void MakeLibraryTile(MenuItemViewModel viewModel)
        {
            viewModel.AccentBrush = MixRadioTileBackground();
            viewModel.CaptionBrush = MixRadioDarkMagenta();
            viewModel.Glyph = null;
            viewModel.ImageOpacity = 0;
            viewModel.IsCommandTile = true;
        }

        private static SolidColorBrush MixRadioDarkCaption()
        {
            return BrushFromArgb(0xD9, 0x00, 0x00, 0x00);
        }

        private static SolidColorBrush MixRadioDarkMagenta()
        {
            return BrushFromArgb(0xFF, 0x8A, 0x12, 0x48);
        }

        private static SolidColorBrush MixRadioGreen()
        {
            return BrushFromArgb(0xFF, 0x78, 0xB8, 0x00);
        }

        private static SolidColorBrush MixRadioPink()
        {
            return BrushFromArgb(0xFF, 0xFF, 0x2B, 0x86);
        }

        private static SolidColorBrush MixRadioTeal()
        {
            return BrushFromArgb(0xFF, 0x00, 0xAF, 0xAF);
        }

        private static SolidColorBrush MixRadioYellow()
        {
            return BrushFromArgb(0xFF, 0xD7, 0xC9, 0x00);
        }

        private static SolidColorBrush MixRadioTileBackground()
        {
            return BrushFromArgb(0xFF, 0x1F, 0x1F, 0x1F);
        }

        #endregion
    }
}
