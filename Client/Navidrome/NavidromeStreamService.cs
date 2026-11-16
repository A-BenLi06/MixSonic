namespace Mixdrome.Navidrome
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Client.Common.Models;
    using Client.Common.Models.Navidrome;
    using Client.Common.Services;
    using Client.Common.Services.Navidrome;
    using Client.Common.Services.DataStructures.SubsonicService;

    public sealed class ResolvedAudioStream
    {
        public Uri Uri { get; set; }
        public bool Negotiated { get; set; }
        public bool Transcoded { get; set; }
        public string Container { get; set; }
        public string Codec { get; set; }
        public bool IsMp3 { get { return string.Equals(Container, "mp3", StringComparison.OrdinalIgnoreCase) || string.Equals(Codec, "mp3", StringComparison.OrdinalIgnoreCase); } }
    }

    public interface INavidromeStreamService
    {
        Task<ResolvedAudioStream> ResolveAsync(PlaylistItem item, CancellationToken cancellation);
    }

    public sealed class NavidromeStreamService : INavidromeStreamService
    {
        private readonly INavidromeSessionService _session;
        private readonly ISubsonicService _subsonic;
        public NavidromeStreamService(INavidromeSessionService session, ISubsonicService subsonic) { _session = session; _subsonic = subsonic; }

        public async Task<ResolvedAudioStream> ResolveAsync(PlaylistItem item, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var config = _subsonic.Configuration;
            var api = new NavidromeApi(config);
            if (item == null || item.AccountKey != api.AccountKey || string.IsNullOrEmpty(item.SongId)) throw new OperationCanceledException();
            var format = config.CompatibleMode ? "mp3" : config.StreamingFormat;
            var maximumKbps = config.MaximumBitRate;
            var secret = config.Password; var authentication = config.AuthenticationMode;
            await _session.ConnectAsync();
            cancellation.ThrowIfCancellationRequested(); RequireSession(api, secret, authentication);
            if (!string.IsNullOrEmpty(format)) return new ResolvedAudioStream { Uri = api.StreamUri(item.SongId, format), Container = format };
            if (_session.Capabilities == null || !_session.Capabilities.Supports("transcoding"))
                return new ResolvedAudioStream { Uri = api.StreamUri(item.SongId, null) };

            var decision = await api.GetTranscodeDecision(item.SongId, ClientPlaybackProfile.Windows81(maximumKbps), cancellation).ReadAsync();
            cancellation.ThrowIfCancellationRequested(); RequireSession(api, secret, authentication);
            if (!decision.CanDirectPlay && !decision.CanTranscode)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(decision.ErrorReason) ? NavidromeText.Get("NoCompatibleStream") : decision.ErrorReason);
            var stream = decision.CanDirectPlay ? decision.SourceStream : decision.TranscodeStream;
            if (stream == null || !string.Equals(stream.Protocol, "http", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(decision.TranscodeParams))
                throw new InvalidOperationException(NavidromeText.Get("InvalidStreamDecision"));
            return new ResolvedAudioStream { Uri = api.TranscodeStreamUri(item.SongId, decision.TranscodeParams), Negotiated = true,
                Transcoded = !decision.CanDirectPlay, Container = stream.Container, Codec = stream.Codec };
        }

        private void RequireSession(NavidromeApi api, string secret, SubsonicAuthenticationMode authentication)
        {
            var config = _subsonic.Configuration;
            if (api.AccountKey != _session.AccountKey || !_session.IsConnected || config == null || config.Password != secret || config.AuthenticationMode != authentication)
                throw new OperationCanceledException();
        }
    }
}
