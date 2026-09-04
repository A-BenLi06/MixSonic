namespace Client.Common.Results
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using System.Xml.Serialization;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.DataStructures.SubsonicService;

    public class GetStarredSongsResult : ServiceResultBase<IList<Song>>, IGetStarredSongsResult
    {
        public GetStarredSongsResult(ISubsonicServiceConfiguration configuration)
            : base(configuration)
        {
        }

        public override string ResourcePath
        {
            get
            {
                return "getStarred2.view";
            }
        }

        public override void HandleResponse(XDocument xDocument)
        {
            var response = xDocument.Element(Namespace + "subsonic-response");
            var starredElement = response == null ? null : response.Element(Namespace + "starred2");
            if (starredElement == null)
            {
                Result = new List<Song>();
                return;
            }

            var xmlSerializer = new XmlSerializer(typeof(Song));
            Result = starredElement.Elements(Namespace + "song")
                .Select(
                    song =>
                    {
                        using (var xmlReader = song.CreateReader())
                        {
                            return (Song)xmlSerializer.Deserialize(xmlReader);
                        }
                    }).ToList();
        }
    }
}
