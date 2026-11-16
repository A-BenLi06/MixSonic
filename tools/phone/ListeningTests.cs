using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Client.Common.Services.DataStructures.SubsonicService;
using Client.Common.Services.Navidrome;
using Mixdrome.Phone.BackgroundAudioTask;
using Mixdrome.Phone.Services;
using Windows.Security.Credentials;
using Windows.Storage;
using Windows.System.Threading;

// Production reporter with native API adapters, controlled HTTP and real temporary files.
// It validates observation/protocol/persistence rules, not device background execution.
public static class PhoneListeningTests
{
    private static int assertions;
    private static void Check(bool value, string message) { ++assertions; if (!value) throw new Exception(message); }
    private static string Response(bool success = true)
    { return "<subsonic-response xmlns='http://subsonic.org/restapi' status='" + (success ? "ok" : "failed") + "' version='1.16.1'/>"; }
    private static PhoneReportingProfile Account(string url, string secret = "secret&音", string user = "fixture", SubsonicAuthenticationMode mode = SubsonicAuthenticationMode.Password)
    {
        ApplicationData.Current.LocalSettings.Values.Clear(); PasswordVault.Credentials.Clear(); PasswordVault.FailNextAdd = false;
        ApplicationData.Current.LocalFolder = new StorageFolder(Path.Combine(Path.GetTempPath(), "mixdrome-listening-" + Guid.NewGuid().ToString("N")));
        var config = new SubsonicServiceConfiguration { BaseUrl = url, PrimaryUrl = url, Username = user, Password = secret, AuthenticationMode = mode };
        PhoneSettings.Save(config); var adapter = new WP8SubsonicService(); adapter.Configure(config); return adapter.CreateReportingProfile();
    }
    private static TrackInfo Track(PhoneReportingProfile profile, string id = "song&音", int duration = 100)
    { return new TrackInfo { Id = id, Duration = duration, StreamUrl = "https://stream.example.com/audio", Reporting = profile }; }
    private static ProtocolRequest[] Submissions(ProtocolServer server)
    { return server.Requests.Where(request => request.Parameter("submission") == "true").ToArray(); }
    private static object Call(object instance, string name, params object[] arguments)
    { return instance.GetType().GetMethod(name, global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic).Invoke(instance, arguments); }
    public static int Main()
    {
        try { Run().GetAwaiter().GetResult(); Console.WriteLine("PASS: " + assertions + " production phone observed-listening, background timer, vault isolation, native-HTTP protocol and durable retry assertions. Native device lifetime remains unverified."); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private static async Task Run()
    {
        using (var server = new ProtocolServer(request => new ProtocolResponse { Body = Response() }))
        {
            var profile = Account(server.Url); double now = 0;
            var sample = new PhoneListeningObservation { Playing = true, Duration = 100 };
            var epoch = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
            var reporter = new PhoneListeningReporter(() => sample, () => now, () => epoch.AddSeconds(now));
            reporter.Start(); reporter.SelectTrack(Track(profile));
            now = 5; ThreadPoolTimer.Fire(); await reporter.DrainAsync();
            Check(server.Requests.Length == 0, "A timer cannot count the previous source before the selected source reports Playing.");
            now = 10; reporter.Observe(true); await reporter.DrainAsync();
            var first = server.Requests.Single();
            Check(first.Parameter("submission") == "false" && first.Parameter("id") == "song&音", "Observed Playing sends presence for the catalog song, without submitting a listen.");
            Check(first.Parameter("time") == NavidromeApi.UnixMilliseconds(epoch.AddSeconds(10)).ToString(), "Reporting uses the actual UTC start rather than the queue request time.");
            Check(first.Parameter("c") == SubsonicRequest.ClientName && first.Parameter("v") == SubsonicRequest.ProtocolVersion, "Native reporting uses shared protocol and client identity.");
            now = 11; sample.Position = 90; reporter.Observe(); await reporter.DrainAsync();
            Check(Submissions(server).Length == 0, "Seeking near the end does not qualify a listen.");
            now = 20; sample.Playing = false; sample.Position = 10; reporter.Observe(true); await reporter.DrainAsync();
            now = 220; reporter.Observe(); await reporter.DrainAsync();
            Check(Submissions(server).Length == 0, "Paused or buffering wall time is excluded.");
            now = 230; sample.Playing = true; reporter.Observe(true); await reporter.DrainAsync();
            now = 270; sample.Position = 50; ThreadPoolTimer.Fire(); await reporter.DrainAsync();
            Check(Submissions(server).Length == 1, "Background timer observation submits after the shared half-duration threshold.");
            Check(Submissions(server).Single().Parameter("time") == first.Parameter("time"), "Presence and qualified submission refer to the same occurrence start.");
            now = 280; reporter.Observe(); await reporter.DrainAsync();
            Check(Submissions(server).Length == 1, "Continued listening does not resubmit an already-qualified occurrence.");
            reporter.SelectTrack(new TrackInfo { Id = "radio", Duration = 0, StreamUrl = "https://radio.example.com" });
            var requests = server.Requests.Length; now = 1000; reporter.Observe(true); await reporter.DrainAsync();
            Check(server.Requests.Length == requests, "External radio IDs are not scrobbled as catalog songs.");
            await reporter.StopAsync();
        }

        var accepting = false;
        using (var server = new ProtocolServer(request => new ProtocolResponse { Body = Response(accepting) }))
        {
            var profile = Account(server.Url, "api-key&音", mode: SubsonicAuthenticationMode.ApiKey); double now = 0;
            var sample = new PhoneListeningObservation { Playing = true, Duration = 10 };
            var epoch = new DateTimeOffset(2026, 11, 16, 1, 0, 0, TimeSpan.Zero);
            var reporter = new PhoneListeningReporter(() => sample, () => now, () => epoch.AddSeconds(now));
            reporter.SelectTrack(Track(profile, duration: 10)); reporter.Observe(true); await reporter.DrainAsync();
            now = 5; sample.Position = 5; reporter.Observe(); await reporter.DrainAsync();
            Check(Submissions(server).Length == 1, "Qualified submissions are attempted even if earlier presence failed.");
            var pendingFile = Path.Combine(ApplicationData.Current.LocalFolder.Path, "MixDromePendingListening.json");
            var serialized = File.ReadAllText(pendingFile);
            Check(!serialized.Contains("api-key&") && !serialized.Contains("StreamUrl") && !serialized.Contains("Password"), "Retry records contain no raw credentials or stream credentials.");
            Check(AudioJson.Deserialize<List<PhoneListenSubmission>>(serialized).Count == 1, "An HTTP-200 failed protocol response retains the qualified record.");
            now = 6; reporter.Observe(); await reporter.DrainAsync();
            Check(Submissions(server).Length == 1, "Failed submissions obey the retry interval.");
            reporter.Finish(); await reporter.StopAsync();
            accepting = true; now = 0;
            var restored = new PhoneListeningReporter(() => new PhoneListeningObservation(), () => now, () => epoch);
            restored.Start(); await restored.DrainAsync();
            Check(Submissions(server).Length == 2 && Submissions(server)[1].Parameter("time") == Submissions(server)[0].Parameter("time"), "Task recreation retries durable qualified listening with its original timestamp.");
            Check(AudioJson.Deserialize<List<PhoneListenSubmission>>(File.ReadAllText(pendingFile)).Count == 0, "A successful response removes the acknowledged retry record.");
            var keyRequest = Submissions(server).Last();
            Check(keyRequest.Parameter("apiKey") == "api-key&音" && keyRequest.Parameter("p") == null && keyRequest.Parameter("u") == null, "Native background HTTP preserves API-key authentication without a password fallback.");
            await restored.StopAsync();
        }

        using (var original = new ProtocolServer(request => new ProtocolResponse { Body = Response() }))
        using (var later = new ProtocolServer(request => new ProtocolResponse { Body = Response() }))
        {
            var profile = Account(original.Url, "original-secret"); double now = 0;
            var sample = new PhoneListeningObservation { Playing = true, Duration = 10 };
            var reporter = new PhoneListeningReporter(() => sample, () => now, () => DateTimeOffset.UtcNow);
            reporter.SelectTrack(Track(profile, duration: 10)); reporter.Observe(true); await reporter.DrainAsync();
            PhoneSettings.Save(new SubsonicServiceConfiguration { BaseUrl = later.Url, PrimaryUrl = later.Url, Username = "fixture", Password = "later-secret" });
            now = 5; reporter.Observe(); await reporter.DrainAsync();
            Check(Submissions(original).Length == 1 && later.Requests.Length == 0, "A playing occurrence stays attached to its captured account when settings change.");
            var expected = new SubsonicServiceConfiguration { Password = "original-secret" }.EncodedPassword;
            Check(Submissions(original).Single().Parameter("p") == expected, "Old-account reporting never uses a later same-username credential.");
            Check(PhoneListeningReporter.LoadCredentials(profile) == null, "A fresh task cannot retrieve a later account's credential for an old server profile.");
            await reporter.StopAsync();
        }

        using (var slow = new ProtocolServer(request => new ProtocolResponse { Body = Response(), DelayMilliseconds = 8000 }))
        {
            var profile = Account(slow.Url); double now = 0;
            var reporter = new PhoneListeningReporter(() => new PhoneListeningObservation { Playing = true, Duration = 100 }, () => now, () => DateTimeOffset.UtcNow);
            reporter.SelectTrack(Track(profile)); reporter.Observe(true);
            Check(!reporter.DrainAsync().IsCompleted, "A slow report remains asynchronous to playback observation.");
            var stopping = reporter.StopAsync();
            Check(await Task.WhenAny(stopping, Task.Delay(2000)) == stopping, "Shutdown cancels bounded HTTP work before releasing background lifetime.");
            await stopping;
        }

        var expectedKey = "old-key";
        using (var server = new ProtocolServer(request => new ProtocolResponse { Body = Response(request.Parameter("apiKey") == expectedKey) }))
        {
            var profile = Account(server.Url, expectedKey, mode: SubsonicAuthenticationMode.ApiKey); double now = 0;
            var reporter = new PhoneListeningReporter(() => new PhoneListeningObservation { Playing = true, Duration = 10 }, () => now, () => DateTimeOffset.UtcNow);
            reporter.SelectTrack(Track(profile, duration: 10)); reporter.Observe(true); await reporter.DrainAsync();
            expectedKey = "rotated-key";
            PhoneSettings.Save(new SubsonicServiceConfiguration { PrimaryUrl = server.Url, BaseUrl = server.Url, Username = "fixture", Password = expectedKey, AuthenticationMode = SubsonicAuthenticationMode.ApiKey });
            now = 5; reporter.Observe(); await reporter.DrainAsync();
            now = 40; reporter.Observe(); await reporter.DrainAsync();
            Check(Submissions(server).Length == 2 && Submissions(server).Last().Parameter("apiKey") == expectedKey, "A failed cached credential is evicted and qualified retry reloads the rotated same-account key.");
            await reporter.StopAsync();
        }
        using (var old = new ProtocolServer(request => new ProtocolResponse { Body = Response(false) }))
        using (var next = new ProtocolServer(request => new ProtocolResponse { Body = Response() }))
        {
            var profile = Account(old.Url); double now = 0;
            var reporter = new PhoneListeningReporter(() => new PhoneListeningObservation { Playing = true, Duration = 10 }, () => now, () => DateTimeOffset.UtcNow);
            reporter.SelectTrack(Track(profile, duration: 10)); reporter.Observe(true); await reporter.DrainAsync();
            now = 5; reporter.Observe(); await reporter.DrainAsync();
            var configuration = new SubsonicServiceConfiguration { PrimaryUrl = next.Url, BaseUrl = next.Url, Username = "fixture", Password = "next-secret" };
            PhoneSettings.Save(configuration); var adapter = new WP8SubsonicService(); adapter.Configure(configuration);
            now = 6; reporter.SelectTrack(Track(adapter.CreateReportingProfile(), "next-song", 10)); reporter.Observe(true); await reporter.DrainAsync();
            now = 11; reporter.Observe(); await reporter.DrainAsync();
            Check(Submissions(old).Length == 1 && Submissions(next).Length == 1, "An old account's cooldown cannot block a qualified new-account submission.");
            await reporter.StopAsync();
        }
        await AudioTaskRaces();
    }

    private static async Task AudioTaskRaces()
    {
        Account("https://example.invalid/");
        var instance = new Windows.ApplicationModel.Background.FakeBackgroundTaskInstance();
        var task = new AudioTask(); task.Run(instance);
        var player = Windows.Media.Playback.BackgroundMediaPlayer.Current;
        Func<string, Task<Windows.Foundation.Collections.ValueSet>> Transfer = async prefix =>
        {
            var name = BackgroundAudioMessages.TransferFilePrefix + Guid.NewGuid().ToString("N") + ".json";
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(name, CreationCollisionOption.FailIfExists);
            await FileIO.WriteTextAsync(file, AudioJson.Serialize(new List<TrackInfo>
            {
                new TrackInfo { Id = prefix + "-first", StreamUrl = "https://example.invalid/" + prefix + "/first" },
                new TrackInfo { Id = prefix + "-second", StreamUrl = "https://example.invalid/" + prefix + "/second" }
            }));
            return new Windows.Foundation.Collections.ValueSet { { BackgroundAudioMessages.QueueFile, name }, { BackgroundAudioMessages.CurrentIndex, 0 } };
        };
        var initial = await Transfer("old");
        await (Task)Call(task, "ExecuteAsync", (Func<Task>)(() => (Task)Call(task, "ReplaceQueueAsync", initial)));
        player.Play();
        var replacement = await Transfer("new");
        var delayedName = (string)replacement[BackgroundAudioMessages.QueueFile];
        var body = File.ReadAllText(Path.Combine(ApplicationData.Current.LocalFolder.Path, delayedName));
        var gate = new TaskCompletionSource<string>();
        FileIO.ReadOverride = file => Path.GetFileName(file.Path) == delayedName ? gate.Task : Task.FromResult(File.ReadAllText(file.Path));
        var replacing = (Task)Call(task, "ExecuteAsync", (Func<Task>)(() => (Task)Call(task, "ReplaceQueueAsync", replacement)));
        Check(!replacing.IsCompleted, "Queue replacement can be held at its real awaited file read.");
        var failures = Windows.Media.Playback.BackgroundMediaPlayer.Failures;
        player.End(); player.Fail(); gate.SetResult(body); await replacing;
        await (Task)Call(task, "ExecuteAsync", (Func<Task>)(() => Task.FromResult(true)));
        Check(player.LastUri.AbsolutePath.EndsWith("new/first"), "A stale ended callback queued behind replacement cannot skip the new first track.");
        Check(Windows.Media.Playback.BackgroundMediaPlayer.Failures == failures, "A stale failed callback cannot mark a replacement source as failed.");

        replacement = await Transfer("canceled"); delayedName = (string)replacement[BackgroundAudioMessages.QueueFile];
        body = File.ReadAllText(Path.Combine(ApplicationData.Current.LocalFolder.Path, delayedName)); gate = new TaskCompletionSource<string>();
        var sourceChanges = player.SourceChanges;
        replacing = (Task)Call(task, "ExecuteAsync", (Func<Task>)(() => (Task)Call(task, "ReplaceQueueAsync", replacement)));
        instance.Cancel();
        Check(!instance.Deferral.Completed, "Shutdown retains its deferral while an earlier queue file operation is active.");
        gate.SetResult(body); await replacing;
        Check(await Task.WhenAny(instance.Deferral.Completion.Task, Task.Delay(2000)) == instance.Deferral.Completion.Task,
            "Shutdown drains the command gate and completes background lifetime after I/O returns.");
        Check(player.SourceChanges == sourceChanges, "Canceled queue I/O cannot assign a native media source after shutdown.");
        FileIO.ReadOverride = null;
    }
}

namespace Windows.Storage
{
    public enum CreationCollisionOption { FailIfExists }
    public enum NameCollisionOption { ReplaceExisting }
    public sealed class StorageFolder
    {
        public string Path;
        public StorageFolder(string path) { Path = path; Directory.CreateDirectory(path); }
        public Task<StorageFile> GetFileAsync(string name)
        { var path = global::System.IO.Path.Combine(Path, name); if (!File.Exists(path)) throw new FileNotFoundException(); return Task.FromResult(new StorageFile(path)); }
        public Task<StorageFile> CreateFileAsync(string name, CreationCollisionOption option)
        { var file = new StorageFile(global::System.IO.Path.Combine(Path, name)); using (File.Open(file.Path, FileMode.CreateNew)) { } return Task.FromResult(file); }
    }
    public sealed class BasicProperties { public ulong Size; }
    public sealed class StorageFile
    {
        public string Path;
        public StorageFile(string path) { Path = path; }
        public Task<BasicProperties> GetBasicPropertiesAsync() { return Task.FromResult(new BasicProperties { Size = (ulong)new FileInfo(Path).Length }); }
        public Task RenameAsync(string name, NameCollisionOption option)
        { var target = global::System.IO.Path.Combine(global::System.IO.Path.GetDirectoryName(Path), name); if (File.Exists(target)) File.Delete(target); File.Move(Path, target); Path = target; return Task.FromResult(true); }
        public Task DeleteAsync() { File.Delete(Path); return Task.FromResult(true); }
    }
    public static class FileIO
    {
        public static Func<StorageFile, Task<string>> ReadOverride;
        public static Task<string> ReadTextAsync(StorageFile file) { return ReadOverride == null ? Task.FromResult(File.ReadAllText(file.Path)) : ReadOverride(file); }
        public static Task WriteTextAsync(StorageFile file, string text) { File.WriteAllText(file.Path, text); return Task.FromResult(true); }
    }
}
namespace Windows.Media.Playback
{
    public enum MediaPlayerState { Playing, Paused, Stopped, Closed, Opening, Buffering }
    public sealed class MediaPlayerFailedEventArgs : EventArgs { }
    public sealed class MediaPlayerDataReceivedEventArgs : EventArgs { public Windows.Foundation.Collections.ValueSet Data; }
    public sealed class MediaPlayer
    {
        public MediaPlayerState CurrentState; public TimeSpan Position, NaturalDuration;
        public bool AutoPlay, CanSeek = true; public Uri LastUri; public int SourceChanges;
        public event Action<MediaPlayer, object> CurrentStateChanged, MediaEnded;
        public event Action<MediaPlayer, MediaPlayerFailedEventArgs> MediaFailed;
        public void SetUriSource(Uri uri) { LastUri = uri; ++SourceChanges; CurrentState = MediaPlayerState.Opening; if (CurrentStateChanged != null) CurrentStateChanged(this, null); }
        public void Play() { CurrentState = MediaPlayerState.Playing; if (CurrentStateChanged != null) CurrentStateChanged(this, null); }
        public void Pause() { CurrentState = MediaPlayerState.Paused; if (CurrentStateChanged != null) CurrentStateChanged(this, null); }
        public void End() { CurrentState = MediaPlayerState.Stopped; if (MediaEnded != null) MediaEnded(this, null); }
        public void Fail() { CurrentState = MediaPlayerState.Stopped; if (MediaFailed != null) MediaFailed(this, new MediaPlayerFailedEventArgs()); }
    }
    public static class BackgroundMediaPlayer
    {
        public static readonly MediaPlayer Current = new MediaPlayer();
        public static int Failures;
        public static event EventHandler<MediaPlayerDataReceivedEventArgs> MessageReceivedFromForeground { add { } remove { } }
        public static void SendMessageToForeground(Windows.Foundation.Collections.ValueSet values)
        { if (values.ContainsKey(BackgroundAudioMessages.Failure)) Interlocked.Increment(ref Failures); }
    }
}
namespace Windows.Foundation.Collections { public sealed class ValueSet : Dictionary<string, object> { } }
namespace Windows.ApplicationModel.Background
{
    public enum BackgroundTaskCancellationReason { Abort }
    public sealed class BackgroundTaskCompletedEventArgs : EventArgs { }
    public sealed class BackgroundTaskRegistration { public event Action<BackgroundTaskRegistration, BackgroundTaskCompletedEventArgs> Completed { add { } remove { } } }
    public sealed class BackgroundTaskDeferral
    {
        public bool Completed; public readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>();
        public void Complete() { Completed = true; Completion.TrySetResult(true); }
    }
    public interface IBackgroundTask { void Run(IBackgroundTaskInstance taskInstance); }
    public interface IBackgroundTaskInstance
    {
        BackgroundTaskDeferral GetDeferral(); BackgroundTaskRegistration Task { get; }
        event Action<IBackgroundTaskInstance, BackgroundTaskCancellationReason> Canceled;
    }
    public sealed class FakeBackgroundTaskInstance : IBackgroundTaskInstance
    {
        public readonly BackgroundTaskDeferral Deferral = new BackgroundTaskDeferral();
        public BackgroundTaskDeferral GetDeferral() { return Deferral; }
        public BackgroundTaskRegistration Task { get; private set; } = new BackgroundTaskRegistration();
        public event Action<IBackgroundTaskInstance, BackgroundTaskCancellationReason> Canceled;
        public void Cancel() { if (Canceled != null) Canceled(this, BackgroundTaskCancellationReason.Abort); }
    }
}
namespace Windows.Media
{
    public enum MediaPlaybackType { Music }
    public enum MediaPlaybackStatus { Playing, Paused, Stopped, Closed, Changing }
    public enum SystemMediaTransportControlsButton { Play, Pause, Next, Previous }
    public sealed class SystemMediaTransportControlsButtonPressedEventArgs : EventArgs { public SystemMediaTransportControlsButton Button; }
    public sealed class MusicProperties { public string Title, Artist, AlbumArtist; }
    public sealed class DisplayUpdater
    {
        public MediaPlaybackType Type; public readonly MusicProperties MusicProperties = new MusicProperties(); public object Thumbnail;
        public void ClearAll() { } public void Update() { }
    }
    public sealed class SystemMediaTransportControls
    {
        public bool IsEnabled, IsPlayEnabled, IsPauseEnabled, IsNextEnabled, IsPreviousEnabled;
        public MediaPlaybackStatus PlaybackStatus; public readonly DisplayUpdater DisplayUpdater = new DisplayUpdater();
        public event Action<SystemMediaTransportControls, SystemMediaTransportControlsButtonPressedEventArgs> ButtonPressed { add { } remove { } }
        public static SystemMediaTransportControls GetForCurrentView() { return new SystemMediaTransportControls(); }
    }
}
namespace Windows.Storage.Streams { public static class RandomAccessStreamReference { public static object CreateFromUri(Uri uri) { return uri; } } }
namespace Windows.System.Threading
{
    public sealed class ThreadPoolTimer
    {
        private static readonly List<ThreadPoolTimer> timers = new List<ThreadPoolTimer>();
        private Action<ThreadPoolTimer> action; private bool canceled;
        public static ThreadPoolTimer CreatePeriodicTimer(Action<ThreadPoolTimer> action, TimeSpan interval)
        { var timer = new ThreadPoolTimer { action = action }; timers.Add(timer); return timer; }
        public void Cancel() { canceled = true; }
        public static void Fire() { foreach (var timer in timers.ToArray()) if (!timer.canceled) timer.action(timer); }
    }
}
namespace Windows.Web.Http
{
    public sealed class NativeOperation<T>
    {
        private readonly Func<CancellationToken, Task<T>> operation;
        public NativeOperation(Func<CancellationToken, Task<T>> operation) { this.operation = operation; }
        public Task<T> AsTask(CancellationToken cancellation) { return operation(cancellation); }
    }
    public sealed class HttpClient : IDisposable
    {
        private readonly global::System.Net.Http.HttpClient client = new global::System.Net.Http.HttpClient();
        public NativeOperation<HttpResponseMessage> GetAsync(Uri uri)
        { return new NativeOperation<HttpResponseMessage>(async cancellation => new HttpResponseMessage(await client.GetAsync(uri, cancellation))); }
        public void Dispose() { client.Dispose(); }
    }
    public sealed class HttpResponseMessage : IDisposable
    {
        private readonly global::System.Net.Http.HttpResponseMessage response;
        public HttpResponseMessage(global::System.Net.Http.HttpResponseMessage response) { this.response = response; Content = new HttpContent(response.Content); }
        public HttpContent Content;
        public void EnsureSuccessStatusCode() { response.EnsureSuccessStatusCode(); }
        public void Dispose() { response.Dispose(); }
    }
    public sealed class HttpContent
    {
        private readonly global::System.Net.Http.HttpContent content;
        public HttpContent(global::System.Net.Http.HttpContent content) { this.content = content; }
        public NativeOperation<string> ReadAsStringAsync() { return new NativeOperation<string>(ignored => content.ReadAsStringAsync()); }
    }
}
