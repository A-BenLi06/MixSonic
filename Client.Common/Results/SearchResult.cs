namespace Client.Common.Results
{
    using System;
    using System.Linq;
    using System.Xml.Linq;
    using System.Xml.Serialization;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.DataStructures.SubsonicService;

    public class SearchResult : ServiceResultBase<SearchResultCollection>, ISearchResult
    {
        #region Fields

        private readonly string _query;

        #endregion

        #region Constructors and Destructors

        public SearchResult(ISubsonicServiceConfiguration configuration, string query)
            : base(configuration)
        {
            _query = query;
        }

        #endregion

        #region Public Properties

        public override string RequestUrl
        {
            get
            {
                return string.Format("{0}&query={1}", base.RequestUrl, Uri.EscapeDataString(_query ?? string.Empty));
            }
        }

        public override string ResourcePath
        {
            get
            {
                return "search3.view";
            }
        }

        #endregion

        #region Methods

        public override void HandleResponse(XDocument xDocument)
        {
            var xmlSerializer = new XmlSerializer(
                typeof(SearchResultCollection), new[] { typeof(ExpandedArtist), typeof(Album), typeof(Song) });
            var searchResultElement = xDocument.Element(Namespace + "subsonic-response")
                .Descendants(Namespace + "searchResult3")
                .FirstOrDefault();

            if (searchResultElement == null)
            {
                Result = new SearchResultCollection();
            }
            else
            {
                using (var xmlReader = searchResultElement.CreateReader())
                {
                    Result = (SearchResultCollection)xmlSerializer.Deserialize(xmlReader);
                }
            }

            Result.Query = _query;
        }

        #endregion
    }
}
