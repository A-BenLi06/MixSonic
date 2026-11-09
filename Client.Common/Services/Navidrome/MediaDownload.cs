namespace Client.Common.Services.Navidrome
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class DownloadProgress
    {
        public long Received { get; set; }
        public long? Total { get; set; }
    }
    public sealed class DownloadPayload
    {
        public long Length { get; set; }
        public bool WindowsCompatible { get; set; }
    }
    public static class MediaDownload
    {
        public static async Task<DownloadPayload> CopyAsync(HttpClient http, Uri uri, Stream destination, CancellationToken cancellation,
            IProgress<DownloadProgress> progress = null, long expectedLength = 0, bool requireMp3 = false)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var type = response.Content.Headers.ContentType == null ? string.Empty : response.Content.Headers.ContentType.MediaType.ToLowerInvariant();
                if (type.StartsWith("text/") || type.Contains("xml") || type.Contains("json"))
                    throw new InvalidDataException("The server returned a document instead of a media file.");
                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var buffer = new byte[65536];
                    var prefix = new byte[4096];
                    var prefixLength = 0;
                    while (prefixLength < prefix.Length)
                    {
                        var read = await source.ReadAsync(prefix, prefixLength, prefix.Length - prefixLength, cancellation).ConfigureAwait(false);
                        if (read == 0) break;
                        prefixLength += read;
                    }
                    if (prefixLength == 0) throw new InvalidDataException("The downloaded media file is empty.");
                    var encoding = prefixLength >= 2 && prefix[0] == 255 && prefix[1] == 254 ? Encoding.Unicode :
                        prefixLength >= 2 && prefix[0] == 254 && prefix[1] == 255 ? Encoding.BigEndianUnicode : Encoding.UTF8;
                    var text = encoding.GetString(prefix, 0, prefixLength).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
                    if (string.IsNullOrWhiteSpace(text) || text.StartsWith("<") || text.StartsWith("{") || text.StartsWith("["))
                        throw new InvalidDataException("The server returned an error document instead of a media file.");
                    var mp3 = IsMp3(prefix, prefixLength);
                    if (requireMp3 && (!mp3 || type.Contains("flac") || type.Contains("ogg")))
                        throw new InvalidDataException("The server did not return the requested compatible MP3 stream.");
                    long received = prefixLength;
                    var total = response.Content.Headers.ContentLength;
                    await destination.WriteAsync(prefix, 0, prefixLength, cancellation).ConfigureAwait(false);
                    if (progress != null) progress.Report(new DownloadProgress { Received = received, Total = total });
                    while (true)
                    {
                        var count = await source.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false);
                        if (count == 0) break;
                        await destination.WriteAsync(buffer, 0, count, cancellation).ConfigureAwait(false);
                        received += count;
                        if (progress != null) progress.Report(new DownloadProgress { Received = received, Total = total });
                    }
                    if ((total.HasValue && total.Value != received) || (expectedLength > 0 && expectedLength != received))
                        throw new InvalidDataException("The downloaded media file is incomplete.");
                    await destination.FlushAsync(cancellation).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    return new DownloadPayload { Length = received, WindowsCompatible = mp3 || IsPcmWave(prefix, prefixLength) };
                }
            }
        }
        private static bool IsMp3(byte[] bytes, int length)
        {
            return length >= 3 && ((bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3') ||
                (bytes[0] == 255 && (bytes[1] & 224) == 224 && (bytes[1] & 6) != 0));
        }
        private static bool IsPcmWave(byte[] bytes, int length)
        {
            if (length < 36 || Encoding.UTF8.GetString(bytes, 0, 4) != "RIFF" || Encoding.UTF8.GetString(bytes, 8, 4) != "WAVE") return false;
            for (var offset = 12; offset + 10 <= length; )
            {
                var size = BitConverter.ToUInt32(bytes, offset + 4);
                if (Encoding.UTF8.GetString(bytes, offset, 4) == "fmt ")
                {
                    var codec = BitConverter.ToUInt16(bytes, offset + 8);
                    return codec == 1 || codec == 3;
                }
                var next = (long)offset + 8 + size + (size & 1);
                if (next > length) break;
                offset = (int)next;
            }
            return false;
        }
    }
}
