using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Client.Common.Models;
using Client.Common.Models.Navidrome;
using Client.Common.Services;
using Client.Common.Services.DataStructures.SubsonicService;
using Client.Common.Services.Navidrome;
using Subsonic8.Navidrome;

public static class NavidromeConnectionTests
{
    private static int _assertions;
    private static void Check(bool value, string message) { _assertions++; if (!value) throw new Exception(message); }
    private static string Response(string body, bool success = true)
    { return "<subsonic-response xmlns='http://subsonic.org/restapi' status='" + (success ? "ok" : "failed") + "' version='1.16.1' type='navidrome'>" + body + "</subsonic-response>"; }
    public static int Main()
    {
        try { Run().GetAwaiter().GetResult(); Console.WriteLine("PASS: " + _assertions + " production connection and stream-service assertions against controlled loopback HTTP. Native decoding remains unverified."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static async Task Run()
    {
        var owner = "fixture"; var decision = Response("<transcodeDecision canDirectPlay='true' canTranscode='false' transcodeParams='fixture-token'><sourceStream protocol='http' container='wav' codec='pcm' audioBitrate='352800'/></transcodeDecision>");
        var advertised = true; var delay = 0;
        using (var bad = new ProtocolServer(request => new ProtocolResponse { Body = Response("<error code='40' message='Authentication failed'/>", false) }))
        using (var good = new ProtocolServer(request =>
        {
            var path = request.Uri.AbsolutePath;
            if (path.EndsWith("getOpenSubsonicExtensions.view")) return new ProtocolResponse { Body = Response(advertised ? "<openSubsonicExtensions name='transcoding'><versions>1</versions></openSubsonicExtensions><openSubsonicExtensions name='apiKeyAuthentication'><versions>1</versions></openSubsonicExtensions>" : "") };
            if (path.EndsWith("getMusicFolders.view")) return new ProtocolResponse { Body = Response("<musicFolders><musicFolder id='1' name='Library'/></musicFolders>") };
            if (path.EndsWith("getUser.view")) return new ProtocolResponse { Body = Response("<user username='fixture' streamRole='true'/>") };
            if (path.EndsWith("tokenInfo.view")) return new ProtocolResponse { Body = Response("<tokenInfo username='" + owner + "'/>") };
            if (path.EndsWith("getTranscodeDecision.view")) return new ProtocolResponse { Body = decision, DelayMilliseconds = delay };
            return new ProtocolResponse { Body = Response(string.Empty) };
        }))
        {
            var routes = new RouteSelectionService(new Network());
            var config = new SubsonicServiceConfiguration { PrimaryUrl = bad.Url, SecondaryUrl = good.Url, Username = "fixture", Password = "fixture" };
            var selected = await routes.SelectBestRouteAsync(config);
            Check(selected.Success && selected.SelectedUrl == good.Url, "A 200 authentication error cannot beat a successfully authenticated route.");
            Check(!(await routes.SelectBestRouteAsync(bad.Url, null, config.Username, config.Password)).Success, "A single 200 failed Subsonic response is rejected.");
            Check(good.Requests.All(request => request.Parameter("c") == SubsonicRequest.ClientName && request.Parameter("v") == SubsonicRequest.ProtocolVersion), "Route discovery identifies the actual client and protocol.");
            config.PrimaryUrl = good.Url; config.SecondaryUrl = null; config.AuthenticationMode = SubsonicAuthenticationMode.ApiKey; config.Password = "fixture-key&+";
            selected = await routes.SelectBestRouteAsync(config);
            Check(selected.Success && good.Requests.Last().Uri.AbsolutePath.EndsWith("tokenInfo.view"), "API-key route discovery checks the returned account identity.");
            var keyRequests = good.Requests.Where(request => request.Parameter("apiKey") != null).ToArray();
            Check(keyRequests.Length == 2 && keyRequests.All(request => request.Parameter("apiKey") == config.Password && request.Parameter("u") == null && request.Parameter("p") == null && !request.Headers.ContainsKey("Authorization")),
                "API-key requests neither encode the key as a password nor mix authentication mechanisms.");
            owner = "another";
            Check(!(await routes.SelectBestRouteAsync(config)).Success, "A key belonging to another account cannot establish a route.");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); var rejected = false;
                try { await routes.SelectBestRouteAsync(config, cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
                Check(rejected, "External route cancellation is preserved.");
            }
            owner = "fixture"; config.BaseUrl = good.Url;
            var subsonic = new SubsonicService { Configuration = config };
            var session = new NavidromeSessionService(subsonic);
            await session.ConnectAsync();
            Check(session.IsConnected && session.User.Username == "fixture", "The production session confirms the API-key account before loading its library.");
            var service = new NavidromeStreamService(session, subsonic);
            var item = new PlaylistItem { SongId = "song&音", AccountKey = session.AccountKey, Uri = subsonic.GetUriForFileWithId("song&音") };
            config.MaximumBitRate = 192;
            var stream = await service.ResolveAsync(item, CancellationToken.None);
            var requestBody = good.Requests.Last();
            using (var json = new MemoryStream(Encoding.UTF8.GetBytes(requestBody.Body)))
            {
                var profile = (ClientPlaybackProfile)new DataContractJsonSerializer(typeof(ClientPlaybackProfile)).ReadObject(json);
                Check(profile.Name == SubsonicRequest.ClientName && profile.MaxAudioBitrate == 192000 && profile.MaxTranscodingAudioBitrate == 192000, "Negotiation submits actual identity and converts kbps to bps.");
                Check(profile.TranscodingProfiles.Single().Container == "mp3" && !profile.DirectPlayProfiles.Any(p => p.Containers.Contains("flac")), "Negotiation advertises native formats and the supported MP3 target.");
            }
            Check(requestBody.Method == "POST" && requestBody.Headers["Content-Type"].StartsWith("application/json") && requestBody.Parameter("mediaId") == item.SongId,
                "The production stream service uses the source-defined JSON POST contract.");
            Check(stream.Negotiated && !stream.Transcoded && stream.Uri.AbsolutePath.EndsWith("getTranscodeStream.view") && new ProtocolRequest { Target = stream.Uri.PathAndQuery, Headers = new System.Collections.Generic.Dictionary<string, string>() }.Parameter("transcodeParams") == "fixture-token",
                "A direct-play decision uses the returned token on the original server.");
            decision = Response("<transcodeDecision canDirectPlay='false' canTranscode='true' transcodeParams='other-token'><transcodeReason>unsupported_codec</transcodeReason><transcodeStream protocol='http' container='mp3' codec='mp3' audioBitrate='192000'/></transcodeDecision>");
            stream = await service.ResolveAsync(item, CancellationToken.None);
            Check(stream.Transcoded && stream.IsMp3, "A transcode decision selects the returned compatible stream.");
            var before = good.Requests.Count(request => request.Uri.AbsolutePath.EndsWith("getTranscodeDecision.view"));
            config.StreamingFormat = "raw";
            stream = await service.ResolveAsync(item, CancellationToken.None);
            Check(!stream.Negotiated && stream.Uri.AbsolutePath.EndsWith("stream.view") && good.Requests.Count(request => request.Uri.AbsolutePath.EndsWith("getTranscodeDecision.view")) == before,
                "An explicit original-format preference bypasses negotiation after confirming the account.");
            config.StreamingFormat = null; advertised = false; await session.ConnectAsync(true);
            stream = await service.ResolveAsync(item, CancellationToken.None);
            Check(!stream.Negotiated && stream.Uri.AbsolutePath.EndsWith("stream.view"), "An unadvertised extension uses the ordinary streaming API.");
            advertised = true; await session.ConnectAsync(true);
            decision = Response("<transcodeDecision canDirectPlay='false' canTranscode='false' errorReason='fixture codec unavailable'/>");
            var failed = false; try { await service.ResolveAsync(item, CancellationToken.None); } catch (InvalidOperationException error) { failed = error.Message == "fixture codec unavailable"; }
            Check(failed, "A denied decision retains its real server error.");
            decision = Response("<transcodeDecision canDirectPlay='true' canTranscode='false'><sourceStream protocol='hls' container='mp3'/></transcodeDecision>");
            failed = false; try { await service.ResolveAsync(item, CancellationToken.None); } catch (InvalidOperationException) { failed = true; }
            Check(failed, "An incomplete or unsupported decision cannot open a fabricated stream.");
            decision = Response("<transcodeDecision canDirectPlay='true' canTranscode='false' transcodeParams='delayed'><sourceStream protocol='http' container='wav'/></transcodeDecision>"); delay = 300;
            using (var cancelled = new CancellationTokenSource())
            {
                var opening = service.ResolveAsync(item, cancelled.Token); await Task.Delay(30); cancelled.Cancel();
                failed = false; try { await opening; } catch (OperationCanceledException) { failed = true; }
                Check(failed, "Cancelling negotiation cancels the actual production HTTP request.");
            }
            config.Username = "another";
            failed = false; try { await service.ResolveAsync(item, CancellationToken.None); } catch (OperationCanceledException) { failed = true; }
            Check(failed, "An old account cannot negotiate with a new account's credentials.");
        }
    }
    private sealed class Network : INetworkDetectionService
    {
        public NetworkConnectionType GetCurrentConnectionType() { return NetworkConnectionType.Ethernet; }
        public event EventHandler<NetworkConnectionType> NetworkChanged { add { } remove { } }
    }
}
