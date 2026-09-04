namespace Client.Common.Results
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using System.Xml.Serialization;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.DataStructures.SubsonicService;

    public class GetInternetRadioStationsResult : ServiceResultBase<IList<InternetRadioStation>>, IGetInternetRadioStationsResult
    {
        public GetInternetRadioStationsResult(ISubsonicServiceConfiguration configuration)
            : base(configuration)
        {
        }

        public override string ResourcePath
        {
            get
            {
                return "getInternetRadioStations.view";
            }
        }

        public override void HandleResponse(XDocument xDocument)
        {
            var response = xDocument.Element(Namespace + "subsonic-response");
            var stationsElement = response == null ? null : response.Element(Namespace + "internetRadioStations");
            if (stationsElement == null)
            {
                Result = new List<InternetRadioStation>();
                return;
            }

            var xmlSerializer = new XmlSerializer(typeof(InternetRadioStation));
            Result = stationsElement.Elements(Namespace + "internetRadioStation")
                .Select(
                    station =>
                    {
                        using (var xmlReader = station.CreateReader())
                        {
                            return (InternetRadioStation)xmlSerializer.Deserialize(xmlReader);
                        }
                    }).ToList();
        }
    }
}
