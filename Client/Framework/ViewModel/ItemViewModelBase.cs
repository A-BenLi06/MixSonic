namespace Subsonic8.Framework.ViewModel
{
    using Caliburn.Micro;
    using Client.Common.Models;
    using Client.Common.Services;

    public class ItemViewModelBase : PropertyChangedBase
    {
        #region Fields

        private string _coverArt;

        private string _coverArtUrl;

        private ISubsonicModel _item;

        private ISubsonicService _subsonicService;

        private string _title;

        #endregion

        #region Public Properties

        /// <summary>
        /// Image source used when the server does not provide cover art for this item
        /// (e.g. the MixRadio pink smiley for artists).
        /// </summary>
        public string CoverArtFallback { get; set; }

        public string CoverArt
        {
            get
            {
                if (_coverArtUrl == null)
                {
                    var url = SubsonicService.GetCoverArtForId(_coverArt);
                    _coverArtUrl = string.IsNullOrEmpty(_coverArt) && !string.IsNullOrEmpty(CoverArtFallback)
                                       ? CoverArtFallback
                                       : url;
                }

                return _coverArtUrl;
            }
        }

        public string CoverArtId
        {
            get
            {
                return _coverArt;
            }

            set
            {
                _coverArt = value;
                _coverArtUrl = null;
                NotifyOfPropertyChange();
                NotifyOfPropertyChange(() => CoverArt);
            }
        }

        public ISubsonicModel Item
        {
            get
            {
                return _item;
            }

            set
            {
                _item = value;
                NotifyOfPropertyChange();
            }
        }

        public ISubsonicService SubsonicService
        {
            get
            {
                return _subsonicService ?? (_subsonicService = IoC.Get<ISubsonicService>());
            }
        }

        public string Title
        {
            get
            {
                return _title;
            }

            set
            {
                _title = value;
                NotifyOfPropertyChange();
            }
        }

        #endregion
    }
}