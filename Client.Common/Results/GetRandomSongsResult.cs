namespace Client.Common.Results
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using System.Xml.Serialization;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.DataStructures.SubsonicService;

    public class GetRandomSongsResult : ServiceResultBase<IList<Song>>, IGetRandomSongsResult
    {
        public GetRandomSongsResult(ISubsonicServiceConfiguration configuration, int numberOfSongs)
            : base(configuration)
        {
            NumberOfSongs = numberOfSongs;
        }

        public override string ResourcePath
        {
            get
            {
                return "getRandomSongs.view";
            }
        }

        public int NumberOfSongs { get; private set; }

        public override string RequestUrl
        {
            get
            {
                return string.Format("{0}&size={1}", base.RequestUrl, NumberOfSongs);
            }
        }

        public override void HandleResponse(XDocument xDocument)
        {
            var response = xDocument.Element(Namespace + "subsonic-response");
            var randomSongsElement = response?.Element(Namespace + "randomSongs");
            if (randomSongsElement == null)
            {
                Result = new List<Song>();
                return;
            }

            var xmlSerializer = new XmlSerializer(typeof(Song));
            Result = randomSongsElement.Elements(Namespace + "song")
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
