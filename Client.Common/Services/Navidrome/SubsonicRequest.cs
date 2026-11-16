namespace Client.Common.Services.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Client.Common.Services.DataStructures.SubsonicService;

    public sealed class SubsonicRequest
    {
        public const string ClientName = "Mixdrome";
        public const string ProtocolVersion = "1.16.1";
        private readonly List<KeyValuePair<string, string>> _parameters = new List<KeyValuePair<string, string>>();

        public SubsonicRequest(ISubsonicServiceConfiguration configuration, string endpoint)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");
            Uri baseUri;
            if (!Uri.TryCreate(configuration.BaseUrl, UriKind.Absolute, out baseUri) ||
                (baseUri.Scheme != "http" && baseUri.Scheme != "https"))
                throw new ArgumentException("A valid HTTP or HTTPS server address is required.", "configuration");
            EndpointUrl = new Uri(baseUri, "rest/" + endpoint + ".view").AbsoluteUri;
            var configured = configuration as SubsonicServiceConfiguration;
            if (configured != null && configured.AuthenticationMode == SubsonicAuthenticationMode.ApiKey)
                Add("apiKey", configuration.Password ?? string.Empty);
            else
            {
                Add("u", configuration.Username ?? string.Empty);
                Add("p", configuration.EncodedPassword);
            }
            Add("v", ProtocolVersion);
            Add("c", ClientName);
        }

        public string EndpointUrl { get; private set; }
        public IEnumerable<KeyValuePair<string, string>> Parameters { get { return _parameters; } }
        public string Url { get { return EndpointUrl + "?" + string.Join("&", _parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))); } }

        public SubsonicRequest Add(string name, string value)
        {
            if (value != null) _parameters.Add(new KeyValuePair<string, string>(name, value));
            return this;
        }

        public SubsonicRequest Add(string name, int value) { return Add(name, value.ToString(CultureInfo.InvariantCulture)); }
        public SubsonicRequest Add(string name, long value) { return Add(name, value.ToString(CultureInfo.InvariantCulture)); }
        public SubsonicRequest Add(string name, double value) { return Add(name, value.ToString("R", CultureInfo.InvariantCulture)); }
        public SubsonicRequest Add(string name, bool value) { return Add(name, value ? "true" : "false"); }
        public SubsonicRequest AddMany(string name, IEnumerable<string> values)
        {
            if (values != null) foreach (var value in values) Add(name, value);
            return this;
        }
    }
}
