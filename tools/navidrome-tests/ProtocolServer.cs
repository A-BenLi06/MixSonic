// A controlled loopback peer for HTTP contracts. It does not render or decode media.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

public sealed class ProtocolRequest
{
    public string Method, Target, Body;
    public Dictionary<string, string> Headers;
    public Uri Uri { get { return new Uri("http://localhost" + Target); } }
    public string Parameter(string key)
    {
        var query = Uri.Query.TrimStart('?');
        if (Headers.ContainsKey("Content-Type") && Headers["Content-Type"].StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) query += "&" + Body;
        return query.Split('&').Select(part => part.Split(new[] { '=' }, 2)).Where(parts => parts.Length == 2 && System.Uri.UnescapeDataString(parts[0]) == key)
            .Select(parts => System.Uri.UnescapeDataString(parts[1].Replace("+", " "))).FirstOrDefault();
    }
}

public sealed class ProtocolResponse
{
    public string Body;
    public int DelayMilliseconds;
    public int Status = 200;
    public string ContentType = "application/xml; charset=utf-8";
}

public sealed class ProtocolServer : IDisposable
{
    private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly Func<ProtocolRequest, ProtocolResponse> _respond;
    private readonly List<TcpClient> _clients = new List<TcpClient>();
    private readonly List<ProtocolRequest> _requests = new List<ProtocolRequest>();
    private readonly List<Task> _serving = new List<Task>();
    private readonly Task _accepting;
    private volatile bool _disposed;
    public string Url { get; private set; }
    public ProtocolRequest[] Requests { get { lock (_requests) return _requests.ToArray(); } }
    public ProtocolServer(Func<ProtocolRequest, ProtocolResponse> respond)
    {
        _respond = respond; _listener.Start();
        Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture) + "/";
        _accepting = Accept();
    }
    private async Task Accept()
    {
        while (!_disposed)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch (SocketException) { if (_disposed) return; throw; }
            catch (ObjectDisposedException) { if (_disposed) return; throw; }
            lock (_clients) _clients.Add(client);
            var serving = Serve(client);
            lock (_serving) _serving.Add(serving);
        }
    }
    private async Task Serve(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var headerBytes = new List<byte>();
                var one = new byte[1];
                while (headerBytes.Count < 16384)
                {
                    if (await stream.ReadAsync(one, 0, 1) == 0) return;
                    headerBytes.Add(one[0]);
                    var count = headerBytes.Count;
                    if (count >= 4 && headerBytes[count - 4] == 13 && headerBytes[count - 3] == 10 && headerBytes[count - 2] == 13 && headerBytes[count - 1] == 10) break;
                }
                var lines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                var first = lines[0].Split(' ');
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1)) { var pair = line.Split(new[] { ':' }, 2); if (pair.Length == 2) headers[pair[0]] = pair[1].Trim(); }
                var length = headers.ContainsKey("Content-Length") ? int.Parse(headers["Content-Length"], CultureInfo.InvariantCulture) : 0;
                if (length < 0 || length > 1024 * 1024) throw new InvalidDataException("Invalid controlled request length.");
                var body = new byte[length]; var read = 0;
                while (read < length) { var count = await stream.ReadAsync(body, read, length - read); if (count == 0) return; read += count; }
                var request = new ProtocolRequest { Method = first[0], Target = first[1], Body = Encoding.UTF8.GetString(body), Headers = headers };
                lock (_requests) _requests.Add(request);
                var response = _respond(request);
                if (response.DelayMilliseconds > 0) await Task.Delay(response.DelayMilliseconds);
                var bytes = Encoding.UTF8.GetBytes(response.Body ?? string.Empty);
                var responseHeader = Encoding.ASCII.GetBytes("HTTP/1.1 " + response.Status.ToString(CultureInfo.InvariantCulture) + " Fixture\r\nContent-Type: " + response.ContentType +
                    "\r\nContent-Length: " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(responseHeader, 0, responseHeader.Length);
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }
        }
        catch (IOException) { }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        finally { lock (_clients) _clients.Remove(client); }
    }
    public void Dispose()
    {
        _disposed = true; _listener.Stop();
        lock (_clients) foreach (var client in _clients.ToArray()) client.Close();
        _accepting.GetAwaiter().GetResult();
        Task[] serving; lock (_serving) serving = _serving.ToArray();
        Task.WhenAll(serving).GetAwaiter().GetResult();
    }
}
