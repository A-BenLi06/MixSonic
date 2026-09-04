namespace Client.Common.Results
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using System.Xml.Serialization;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.DataStructures.SubsonicService;

    public class GetGenresResult : ServiceResultBase<IList<Genre>>, IGetGenresResult
    {
        #region Constructors and Destructors

        public GetGenresResult(ISubsonicServiceConfiguration configuration)
            : base(configuration)
        {
        }

        #endregion

        #region Public Properties

        public override string ResourcePath
        {
            get
            {
                return "getGenres.view";
            }
        }

        #endregion

        #region Methods

        public override void HandleResponse(XDocument xDocument)
        {
            var response = xDocument.Element(Namespace + "subsonic-response");
            var genresElement = response?.Element(Namespace + "genres");
            if (genresElement == null)
            {
                Result = new List<Genre>();
                return;
            }

            var xmlSerializer = new XmlSerializer(typeof(Genre));
            Result = genresElement.Elements(Namespace + "genre")
                .Select(
                    genreElement =>
                    {
                        using (var xmlReader = genreElement.CreateReader())
                        {
                            var genre = (Genre)xmlSerializer.Deserialize(xmlReader);
                            genre.Id = genre.Name;
                            return genre;
                        }
                    })
                .ToList();
        }

        #endregion
    }
}
