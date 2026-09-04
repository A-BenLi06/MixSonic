namespace Client.Common.Results
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using System.Xml.Serialization;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.DataStructures.SubsonicService;

    public class GetNowPlayingResult : ServiceResultBase<IList<NowPlayingEntry>>, IGetNowPlayingResult
    {
        public GetNowPlayingResult(ISubsonicServiceConfiguration configuration)
            : base(configuration)
        {
        }

        public override string ResourcePath
        {
            get
            {
                return "getNowPlaying.view";
            }
        }

        public override void HandleResponse(XDocument xDocument)
        {
            var response = xDocument.Element(Namespace + "subsonic-response");
            var nowPlayingElement = response == null ? null : response.Element(Namespace + "nowPlaying");
            if (nowPlayingElement == null)
            {
                Result = new List<NowPlayingEntry>();
                return;
            }

            var xmlSerializer = new XmlSerializer(typeof(NowPlayingEntry));
            Result = nowPlayingElement.Elements(Namespace + "entry")
                .Select(
                    entry =>
                    {
                        using (var xmlReader = entry.CreateReader())
                        {
                            return (NowPlayingEntry)xmlSerializer.Deserialize(xmlReader);
                        }
                    }).ToList();
        }
    }
}
