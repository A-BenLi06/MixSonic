namespace Client.Common.Models.Subsonic
{
    using System.Xml.Serialization;

    [XmlRoot(ElementName = "entry", Namespace = "http://subsonic.org/restapi")]
    public class PlaylistEntry : Song
    {
        // Retain the old client property while sharing the server song contract.
        [XmlIgnore]
        public string ParentId { get { return Parent; } set { Parent = value; } }
    }
}
