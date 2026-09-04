namespace Client.Common.Results
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using System.Xml.Serialization;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.DataStructures.SubsonicService;

    public class GetAllArtistsResult : ServiceResultBase<IList<ExpandedArtist>>, IGetAllArtistsResult
    {
        #region Constructors and Destructors

        public GetAllArtistsResult(ISubsonicServiceConfiguration configuration)
            : base(configuration)
        {
        }

        #endregion

        #region Public Properties

        public override string ResourcePath
        {
            get
            {
                return "getArtists.view";
            }
        }

        #endregion

        #region Methods

        public override void HandleResponse(XDocument xDocument)
        {
            var response = xDocument.Element(Namespace + "subsonic-response");
            var artistsElement = response?.Element(Namespace + "artists");
            if (artistsElement == null)
            {
                Result = new List<ExpandedArtist>();
                return;
            }

            var xmlSerializer = new XmlSerializer(typeof(ExpandedArtist), new[] { typeof(Album) });
            Result = artistsElement.Descendants(Namespace + "artist")
                .Select(
                    artist =>
                    {
                        using (var xmlReader = artist.CreateReader())
                        {
                            return (ExpandedArtist)xmlSerializer.Deserialize(xmlReader);
                        }
                    })
                .ToList();
        }

        #endregion
    }
}
