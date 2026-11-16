using System;
using System.Collections.Generic;
using Client.Common.Services.DataStructures.SubsonicService;
using Mixdrome.Phone.Services;

// ResolveArtwork is extracted verbatim from PlaybackPage by run-connection.ps1.
// The service and SubsonicRequest are production types, so opaque identifiers
// must survive the complete URI classification and protocol escaping path.
public static class PhoneArtworkTests
{
    private static int assertions;
    private static void Check(bool condition, string message)
    { ++assertions; if (!condition) throw new Exception(message); }

    public static int Main()
    {
        try
        {
            var service = new WP8SubsonicService();
            service.Configure(new SubsonicServiceConfiguration
            {
                BaseUrl = "https://music.example.invalid/navidrome/",
                Username = "fixture", Password = "fixture-password"
            });
            foreach (var id in new[] { "dc-opaque-cover:1", "al-opaque-cover:1", "cover&音" })
            {
                var resolved = new Uri(ResolveArtwork(id, service), UriKind.Absolute);
                var request = new ProtocolRequest { Target = resolved.PathAndQuery, Headers = new Dictionary<string, string>() };
                Check(resolved.Scheme == "https" && request.Uri.AbsolutePath.EndsWith("/rest/getCoverArt.view"),
                    "Opaque cover IDs resolve through the configured cover-art endpoint.");
                Check(request.Parameter("id") == id && resolved.AbsoluteUri.Contains(Uri.EscapeDataString(id)),
                    "Opaque cover IDs, including colon versions and Unicode, retain their exact value after escaping.");
            }
            foreach (var address in new[] {
                "http://images.example.invalid/cover.png",
                "https://images.example.invalid/cover.png?size=300",
                "ms-appx:///Assets/MixRadio/DefaultArtwork300.png",
                "ms-appdata:///local/covers/fixture.png"
            })
                Check(ResolveArtwork(address, service) == new Uri(address, UriKind.Absolute).ToString(),
                    "Supported web and application artwork URIs remain direct image sources.");
            foreach (var empty in new[] { null, string.Empty })
                Check(ResolveArtwork(empty, service) == "ms-appx:///Assets/MixRadio/DefaultArtwork300.png",
                    "Missing artwork retains the packaged fallback.");
            var unsupported = new ProtocolRequest
            {
                Target = new Uri(ResolveArtwork("file:///unsupported-cover", service)).PathAndQuery,
                Headers = new Dictionary<string, string>()
            };
            Check(unsupported.Parameter("id") == "file:///unsupported-cover",
                "An unsupported URI scheme is treated as an opaque server cover ID.");
            Console.WriteLine("PASS: " + assertions + " production phone artwork URI/opaque-ID assertions. Native image decoding remains separate.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    /* PRODUCTION_ARTWORK_METHOD */
}
