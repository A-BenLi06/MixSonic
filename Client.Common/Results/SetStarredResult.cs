namespace Client.Common.Results
{
    using System;
    using System.Net;
    using System.Xml.Linq;
    using Client.Common.Services.DataStructures.SubsonicService;

    public class SetStarredResult : ServiceResultBase<bool>, ISetStarredResult
    {
        private readonly string _songId;
        private readonly bool _starred;

        public SetStarredResult(ISubsonicServiceConfiguration configuration, string songId, bool starred)
            : base(configuration)
        {
            if (string.IsNullOrWhiteSpace(songId)) throw new ArgumentException("A song ID is required.", "songId");
            _songId = songId;
            _starred = starred;
        }

        public override string ResourcePath
        {
            get { return _starred ? "star.view" : "unstar.view"; }
        }

        public override string RequestUrl
        {
            get { return base.RequestUrl + "&id=" + WebUtility.UrlEncode(_songId); }
        }

        public override void HandleResponse(XDocument document)
        {
            var response = document.Element(Namespace + "subsonic-response");
            Result = response != null && (string)response.Attribute("status") == "ok";
        }
    }
}
