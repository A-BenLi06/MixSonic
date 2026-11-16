using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Caliburn.Micro;
using Client.Common.Models;
using Client.Common.Models.Navidrome;
using Client.Common.Models.Subsonic;
using Client.Common.Services;
using Client.Common.Services.DataStructures.SubsonicService;
using Client.Common.Services.Navidrome;
using Mixdrome.Navidrome;
using Windows.UI.Xaml;

public static class NavidromeJukeboxTests
{
    private static int _assertions;
    private static void Check(bool value, string message) { _assertions++; if (!value) throw new Exception(message); }
    public static int Main()
    {
        try
        {
            using (var pump = new Pump()) pump.Run(Run);
            Console.WriteLine("PASS: " + _assertions + " production jukebox page assertions against controlled loopback HTTP responses. Native UI/server audio remain unverified.");
            Console.WriteLine("ND-CHECK:jukebox:remote-player");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static async Task Timely(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new TimeoutException("The controlled jukebox request did not complete.");
        await task;
    }
    private static JukeboxState Box(int current = 2, int position = 12, double gain = 0.2, params string[] ids)
    {
        return new JukeboxState { CurrentIndex = current, Position = position, Gain = gain,
            Songs = (ids.Length == 0 ? new[] { "s", "a", "s" } : ids).Select(id => new Song { Id = id, Title = "Song " + id, Artist = "Fixture", Duration = 120 }).ToList() };
    }
    private static Task Poll(NavidromeJukeboxViewModel page)
    { return (Task)typeof(NavidromeJukeboxViewModel).GetMethod("Poll", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page, null); }
    private static void Active(NavidromeJukeboxViewModel page, bool value)
    { typeof(NavidromePageViewModel).GetField("PageActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(page, value); }
    private static async Task Run()
    {
        using (var server = new Responses())
        {
            var session = new Session(server.BaseUrl); var events = new Events(); var dialogs = new Dialogs();
            var queue = new PlaylistManagementService(events);
            var page = new NavidromeJukeboxViewModel { Session = session, PlaylistQueue = queue, Dialogs = dialogs, EventAggregator = events };
            Active(page, true); await page.Reload();
            Check(page.Tracks.Count == 3 && page.Tracks[0].Media.Id == page.Tracks[2].Media.Id && page.Position == "0:12", "Initial remote queue retains duplicate occurrences and seconds.");
            Check(page.CanStart && !page.CanStop && page.Gain == 0.2, "Commands follow returned remote playback state.");
            await page.Start(); Check(page.CanStop && !page.CanStart, "Start refreshes actual returned playback state.");
            await page.Stop(); Check(page.CanStart, "Stop refreshes remote transport state.");

            server.State = Box(gain: 0.6); await Poll(page);
            Check(page.Gain == 0.6, "Remote volume changes update an unedited control.");
            page.Gain = 0.4; server.State = Box(gain: 0.3); await Poll(page);
            Check(page.Gain == 0.4, "Polling preserves a volume draft until the user applies it.");
            await page.SetGain(); Check(server.State.Gain == 0.4 && page.Gain == 0.4, "Applying volume sends the captured gain and reads back server state.");
            server.State = Box(gain: 0.7); await Poll(page);
            Check(page.Gain == 0.7, "The volume draft clears after a confirmed update.");

            var observed = new TaskCompletionSource<bool>(); var release = new TaskCompletionSource<string>(); var hold = true;
            server.Handler = request =>
            {
                if (request.Action == "get" && hold) { hold = false; observed.SetResult(true); return release.Task; }
                return Task.FromResult(server.Respond(request));
            };
            var stalePoll = Poll(page); await Timely(observed.Task);
            server.State = Box(position: 90, gain: 0.1); page.Gain = 0.5;
            await page.SetGain(); release.SetResult(Responses.Xml(Box(position: 1, gain: 0.2))); await Timely(stalePoll);
            Check(page.Position == "1:30" && page.Gain == 0.5, "A delayed poll cannot overwrite a newer command's position or gain.");
            server.Handler = null;

            server.State = Box(); await page.Reload(); page.SelectedTrack = page.Tracks[2]; await page.Remove();
            Check(server.Requests.Last(request => request.Action == "remove").Value("index") == "2" && page.Tracks.Count == 2,
                "Removing a repeated song targets the selected occurrence by index.");
            server.State = Box(); await page.Reload(); page.SelectedTrack = page.Tracks[1]; var mutations = server.Mutations;
            server.State = Box(ids: new[] { "s", "b", "s" }); await page.Remove();
            Check(server.Mutations == mutations && page.Tracks[1].Media.Id == "b" && page.Error != null, "Changed queue order is refreshed before refusing a stale indexed removal.");
            server.State = Box(current: 0); await page.Reload(); mutations = server.Mutations;
            server.State = Box(current: 1); await page.Next();
            Check(server.Mutations == mutations && page.CurrentSong.Contains("Song a"), "Relative skips refuse a changed current track even when queue order is unchanged.");

            server.State = Box(); await page.Reload(); mutations = server.Mutations;
            var confirm = new TaskCompletionSource<bool>(); dialogs.Form = (fields, validate) => { fields[0].Value = "9"; return confirm.Task; };
            var seek = page.Seek(new FrameworkElement()); server.State = Box(current: 1); confirm.SetResult(true); await seek;
            Check(server.Mutations == mutations && page.CurrentSong.Contains("Song a") && page.Error != null, "Seeking rechecks the current track after a delayed form.");
            dialogs.Form = (fields, validate) => { fields[0].Value = "9"; return Task.FromResult(validate() == null); };
            await page.Seek(new FrameworkElement());
            Check(server.Requests.Last(request => request.Action == "skip").Value("offset") == "9" && page.Position == "0:09", "Confirmed seeking sends whole seconds and refreshes the position.");
            mutations = server.Mutations;
            dialogs.Form = (fields, validate) => { fields[0].Value = "121"; Check(validate() != null, "A seek outside the known song duration is rejected in the form."); return Task.FromResult(false); };
            await page.Seek(new FrameworkElement()); Check(server.Mutations == mutations, "Rejected seek input sends no mutation.");
            dialogs.Form = null;

            mutations = server.Mutations; confirm = new TaskCompletionSource<bool>(); dialogs.Confirm = () => confirm.Task;
            var clear = page.Clear(); server.State = Box(ids: new[] { "new", "a", "s" }); confirm.SetResult(true); await clear;
            Check(server.Mutations == mutations && page.Tracks[0].Media.Id == "new", "Clear refuses to erase a queue changed while confirmation was open.");
            dialogs.Confirm = null;
            queue.Items.Add(new PlaylistItem { SongId = "s", AccountKey = session.AccountKey, Uri = new Uri(server.BaseUrl) });
            queue.Items.Add(new PlaylistItem { SongId = "s", AccountKey = session.AccountKey, Uri = new Uri(server.BaseUrl) });
            await page.UseQueue(); Check(page.Tracks.Count == 2 && page.Tracks.All(track => track.Media.Id == "s"), "Replacing the remote queue preserves repeated local songs.");
            await page.AppendQueue(); Check(page.Tracks.Count == 4, "Appending adds local occurrences without replacing the remote queue.");
            queue.Items.Add(new PlaylistItem { SongId = "foreign", AccountKey = "other", Uri = new Uri(server.BaseUrl) }); mutations = server.Mutations;
            await page.AppendQueue(); Check(!page.CanAppendQueue && server.Mutations == mutations, "Mixed-account local queues cannot be sent to the remote player.");
            await page.Clear(); Check(page.IsEmpty && !page.CanStart && page.Tracks.Count == 0, "A confirmed clear produces a usable empty remote queue.");

            server.State = Box(current: int.MaxValue); await page.Reload(); mutations = server.Mutations;
            Check(!page.CanSeek && !page.CanPrevious && !page.CanNext, "An invalid returned current index cannot enable indexed transport commands.");
            await page.Next(); Check(server.Mutations == mutations, "An invalid returned current index cannot overflow into a remote skip request.");

            server.State = Box(); await page.Reload(); mutations = server.Mutations;
            confirm = new TaskCompletionSource<bool>(); dialogs.Form = (fields, validate) => { fields[0].Value = "3"; return confirm.Task; };
            seek = page.Seek(new FrameworkElement()); session.Configuration.Username = "another account"; confirm.SetResult(true); await seek;
            Check(server.Mutations == mutations && !page.CanSeek, "A changed account cancels a form before any remote mutation.");
            dialogs.Form = null; await page.Reload(); mutations = server.Mutations;
            confirm = new TaskCompletionSource<bool>(); dialogs.Confirm = () => confirm.Task;
            clear = page.Clear(); Active(page, false); confirm.SetResult(true); await clear;
            Check(server.Mutations == mutations, "Leaving the page cancels a pending clear operation.");
            Active(page, true); dialogs.Confirm = null;
            session.User.JukeboxRole = false; var requests = server.Requests.Count; await page.Reload();
            Check(page.Error != null && !page.CanStart && server.Requests.Count == requests, "A denied jukebox role prevents remote player requests and shows its unavailable state.");
            session.User.JukeboxRole = true; await page.Reload(); page.Gain = 0.9;
            server.Handler = request => Task.FromResult(request.Action == "setGain" ? "<subsonic-response xmlns='http://subsonic.org/restapi' status='failed'><error code='0' message='fixture denied gain'/></subsonic-response>" : server.Respond(request));
            await page.SetGain(); await Poll(page);
            Check(page.Error != null && page.Error.Contains("fixture denied gain") && page.Gain == 0.9, "A failed volume mutation retains the explicit error and the user's draft across successful polling.");
            Check(server.Failure == null, "The controlled HTTP fixture completed without transport failures.");
        }
    }

    private sealed class Session : PropertyChangedBase, INavidromeSessionService
    {
        public Session(string url) { Configuration = new SubsonicServiceConfiguration { BaseUrl = url, Username = "jukebox-fixture", Password = "fixture" }; User = new ServerUser { JukeboxRole = true }; Folders = new BindableCollection<MusicFolder>(); }
        public SubsonicServiceConfiguration Configuration;
        public event EventHandler LibraryChanged { add { } remove { } }
        public ServerCapabilities Capabilities { get { return new ServerCapabilities(); } }
        public ServerUser User { get; private set; }
        public BindableCollection<MusicFolder> Folders { get; private set; }
        public MusicFolder SelectedFolder { get; set; }
        public string FolderId { get { return null; } }
        public string AccountKey { get { return AccountIdentity.For(Configuration); } }
        public string Status { get { return null; } }
        public string CapabilityError { get { return null; } }
        public string UserError { get { return null; } }
        public bool IsConnected { get { return true; } }
        public Task ConnectAsync(bool refresh = false) { return Task.FromResult(true); }
        public NavidromeApi CreateApi() { return new NavidromeApi(Configuration); }
    }
    private sealed class Dialogs : INavidromeDialogService
    {
        public Func<IList<FormField>, Func<string>, Task<bool>> Form;
        public Func<Task<bool>> Confirm;
        public Task<bool> FormAsync(FrameworkElement anchor, string titleKey, IList<FormField> fields, Func<string> validate = null)
        { return Form == null ? Task.FromResult(false) : Form(fields, validate); }
        public Task<bool> ConfirmAsync(string messageKey, string confirmKey = "Delete") { return Confirm == null ? Task.FromResult(true) : Confirm(); }
    }
    private sealed class Events : IEventAggregator
    {
        public bool HandlerExistsFor(Type type) { return false; }
        public void Subscribe(object target) { }
        public void Unsubscribe(object target) { }
        public void Publish(object message, Action<Action> marshal) { }
    }
    private sealed class Pump : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<Action> _work = new BlockingCollection<Action>();
        public override void Post(SendOrPostCallback callback, object state) { _work.Add(() => callback(state)); }
        public void Run(Func<Task> work)
        {
            SetSynchronizationContext(this); var task = work();
            while (!task.IsCompleted) { Action next; if (_work.TryTake(out next, 100)) next(); }
            task.GetAwaiter().GetResult(); SetSynchronizationContext(null);
        }
        public void Dispose() { _work.Dispose(); }
    }
    private sealed class Request
    {
        public IList<KeyValuePair<string, string>> Parameters;
        public string Action { get { return Value("action"); } }
        public string Value(string name) { return Parameters.Where(pair => pair.Key == name).Select(pair => pair.Value).FirstOrDefault(); }
    }
    private sealed class Responses : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly ConcurrentBag<TcpClient> _clients = new ConcurrentBag<TcpClient>();
        private readonly object _gate = new object();
        private JukeboxState _state = Box();
        public readonly ConcurrentQueue<Request> Requests = new ConcurrentQueue<Request>();
        public Func<Request, Task<string>> Handler;
        public Exception Failure;
        public string BaseUrl;
        public JukeboxState State { get { lock (_gate) return _state; } set { lock (_gate) _state = value; } }
        public int Mutations { get { return Requests.Count(request => request.Action != "get" && request.Action != "status"); } }
        public Responses() { _listener.Start(); BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/"; Accept(); }
        private async void Accept()
        {
            try { while (true) { var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); _clients.Add(client); Serve(client); } }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }
        private async void Serve(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                {
                    var start = await reader.ReadLineAsync().ConfigureAwait(false); if (start == null) return;
                    var length = 0; string line; var expectContinue = false;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15).Trim(), CultureInfo.InvariantCulture);
                        if (line.StartsWith("Expect:", StringComparison.OrdinalIgnoreCase)) expectContinue = true;
                    }
                    if (expectContinue) { var interim = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"); await stream.WriteAsync(interim, 0, interim.Length).ConfigureAwait(false); }
                    var buffer = new char[length]; var read = 0;
                    while (read < length) { var count = await reader.ReadAsync(buffer, read, length - read).ConfigureAwait(false); if (count == 0) throw new EndOfStreamException(); read += count; }
                    var uri = new Uri(new Uri(BaseUrl), start.Split(' ')[1]);
                    var parameters = (uri.Query.TrimStart('?') + "&" + new string(buffer)).Split('&').Where(part => part.Contains("="))
                        .Select(part => part.Split(new[] { '=' }, 2)).Select(pair => new KeyValuePair<string, string>(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1].Replace("+", " ")))).ToList();
                    var request = new Request { Parameters = parameters }; Requests.Enqueue(request);
                    var handler = Handler; var response = handler == null ? Respond(request) : await handler(request).ConfigureAwait(false);
                    var bytes = Encoding.UTF8.GetBytes(response);
                    var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/xml\r\nContent-Length: " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false); await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                }
            }
            catch (Exception error) { Failure = error; }
        }
        public string Respond(Request request)
        {
            lock (_gate)
            {
                switch (request.Action)
                {
                    case "start": _state.Playing = true; break;
                    case "stop": _state.Playing = false; break;
                    case "setGain": _state.Gain = double.Parse(request.Value("gain"), CultureInfo.InvariantCulture); break;
                    case "skip": _state.CurrentIndex = int.Parse(request.Value("index"), CultureInfo.InvariantCulture); _state.Position = int.Parse(request.Value("offset"), CultureInfo.InvariantCulture); break;
                    case "remove": _state.Songs.RemoveAt(int.Parse(request.Value("index"), CultureInfo.InvariantCulture)); _state.CurrentIndex = Math.Min(_state.CurrentIndex, _state.Songs.Count - 1); break;
                    case "clear": _state.Songs.Clear(); _state.CurrentIndex = -1; _state.Playing = false; break;
                    case "set": _state.Songs.Clear(); goto case "add";
                    case "add": foreach (var id in request.Parameters.Where(pair => pair.Key == "id").Select(pair => pair.Value)) _state.Songs.Add(new Song { Id = id, Title = "Song " + id, Duration = 120 }); break;
                }
                return Xml(_state, request.Action == "get");
            }
        }
        public static string Xml(JukeboxState state, bool playlist = true)
        {
            XNamespace ns = "http://subsonic.org/restapi";
            var node = new XElement(ns + (playlist ? "jukeboxPlaylist" : "jukeboxStatus"), new XAttribute("playing", state.Playing), new XAttribute("currentIndex", state.CurrentIndex),
                new XAttribute("position", state.Position), new XAttribute("gain", state.Gain));
            if (playlist) node.Add(state.Songs.Select(song => new XElement(ns + "entry", new XAttribute("id", song.Id), new XAttribute("title", song.Title), new XAttribute("duration", song.Duration))));
            return new XElement(ns + "subsonic-response", new XAttribute("status", "ok"), node).ToString(SaveOptions.DisableFormatting);
        }
        public void Dispose() { _listener.Stop(); foreach (var client in _clients) client.Close(); }
    }
}
