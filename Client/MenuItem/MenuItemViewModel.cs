namespace Subsonic8.MenuItem
{
    using Subsonic8.Framework.ViewModel;
    using Windows.UI.Xaml.Media;

    public class MenuItemViewModel : ItemViewModelBase, IMenuItemViewModel
    {
        #region Fields

        private Brush _accentBrush;

        private Brush _captionBrush;

        private string _glyph;

        private double _imageOpacity;

        private bool _isCommandTile;

        private string _subtitle;

        private double _tileHeight;

        private double _tileWidth;

        private string _type;

        #endregion

        #region Constructors and Destructors

        public MenuItemViewModel()
        {
            ImageOpacity = 1;
            TileWidth = 170;
            TileHeight = 170;
        }

        #endregion

        #region Public Properties

        public Brush AccentBrush
        {
            get
            {
                return _accentBrush;
            }

            set
            {
                _accentBrush = value;
                NotifyOfPropertyChange();
            }
        }

        public Brush CaptionBrush
        {
            get
            {
                return _captionBrush;
            }

            set
            {
                _captionBrush = value;
                NotifyOfPropertyChange();
            }
        }

        public string Glyph
        {
            get
            {
                return _glyph;
            }

            set
            {
                _glyph = value;
                NotifyOfPropertyChange();
            }
        }

        public double ImageOpacity
        {
            get
            {
                return _imageOpacity;
            }

            set
            {
                _imageOpacity = value;
                NotifyOfPropertyChange();
            }
        }

        public bool IsCommandTile
        {
            get
            {
                return _isCommandTile;
            }

            set
            {
                _isCommandTile = value;
                NotifyOfPropertyChange();
            }
        }

        public string Subtitle
        {
            get
            {
                return _subtitle;
            }

            set
            {
                _subtitle = value;
                NotifyOfPropertyChange();
            }
        }

        public double TileHeight
        {
            get
            {
                return _tileHeight;
            }

            set
            {
                _tileHeight = value;
                NotifyOfPropertyChange();
            }
        }

        public double TileWidth
        {
            get
            {
                return _tileWidth;
            }

            set
            {
                _tileWidth = value;
                NotifyOfPropertyChange();
            }
        }

        public string Type
        {
            get
            {
                return _type;
            }

            set
            {
                _type = value;
                NotifyOfPropertyChange(() => Type);
            }
        }

        #endregion
    }
}
