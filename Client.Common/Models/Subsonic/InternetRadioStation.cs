namespace Client.Common.Models.Subsonic
{
    using System;
    using System.Runtime.Serialization;
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "internetRadioStation", Namespace = "http://subsonic.org/restapi")]
    [DataContract]
    public class InternetRadioStation : MediaModelBase
    {
        [XmlAttribute("streamUrl")]
        [DataMember]
        public string StreamUrl { get; set; }

        [XmlAttribute("homePageUrl")]
        [DataMember]
        public string HomePageUrl { get; set; }

        public override SubsonicModelTypeEnum Type
        {
            get
            {
                return SubsonicModelTypeEnum.InternetRadioStation;
            }
        }

        public override Tuple<string, string> GetDescription()
        {
            var subtitle = string.IsNullOrEmpty(HomePageUrl) ? "internet radio" : HomePageUrl;
            return new Tuple<string, string>(Name, subtitle);
        }
    }
}
