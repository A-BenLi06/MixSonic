namespace Client.Common.Results
{
    using System;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Client.Common.Services.Navidrome;
    using Client.Common.Models.Navidrome;

    public sealed class NavidromeResult<T> : ServiceResultBase<T>
    {
        private readonly SubsonicRequest _request;
        private readonly Func<XElement, T> _read;
        private readonly bool _post;
        private readonly string _json;
        private readonly CancellationToken _cancellation;

        public NavidromeResult(ISubsonicServiceConfiguration configuration, SubsonicRequest request, Func<XElement, T> read, bool post = false,
            string json = null, CancellationToken cancellation = default(CancellationToken))
            : base(configuration) { _request = request; _read = read; _post = post; _json = json; _cancellation = cancellation; }

        public override string ResourcePath { get { return string.Empty; } }
        public override string RequestUrl { get { return _request.Url; } }
        public ServerCapabilities Server { get; private set; }
        protected override CancellationToken RequestCancellationToken { get { return _cancellation; } }

        protected override HttpRequestMessage CreateRequest()
        {
            if (_json != null) return new HttpRequestMessage(HttpMethod.Post, RequestUrl) { Content = new StringContent(_json, Encoding.UTF8, "application/json") };
            return _post ? new HttpRequestMessage(HttpMethod.Post, _request.EndpointUrl) { Content = new FormUrlEncodedContent(_request.Parameters) }
                         : new HttpRequestMessage(HttpMethod.Get, RequestUrl);
        }

        public override void HandleResponse(XDocument document)
        {
            var root = document.Element(Namespace + "subsonic-response");
            if (root == null || (string)root.Attribute("status") != "ok") throw new InvalidOperationException("Invalid Subsonic response.");
            Server = new ServerCapabilities
            {
                Type = (string)root.Attribute("type"), ServerVersion = (string)root.Attribute("serverVersion"),
                ProtocolVersion = (string)root.Attribute("version"), OpenSubsonic = (bool?)root.Attribute("openSubsonic") ?? false
            };
            Result = _read(root);
        }

        public async Task<T> ReadAsync()
        {
            await Execute();
            if (Error != null) throw Error;
            return Result;
        }
    }
}
