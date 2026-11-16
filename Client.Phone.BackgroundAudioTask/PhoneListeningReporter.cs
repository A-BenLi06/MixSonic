namespace Mixdrome.Phone.BackgroundAudioTask
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;
    using System.Xml.Linq;
    using Client.Common.Models;
    using Client.Common.Results;
    using Client.Common.Services;
    using Client.Common.Services.DataStructures.SubsonicService;
    using Client.Common.Services.Navidrome;
    using Mixdrome.Phone.Services;
    using Windows.Media.Playback;
    using Windows.Security.Credentials;
    using Windows.Storage;
    using Windows.System.Threading;
    using Windows.Web.Http;

    internal sealed class PhoneListeningObservation
    {
        public bool Playing;
        public double Position;
        public double Duration;
    }

    internal sealed class PhoneListeningReporter
    {
        private sealed class Occurrence
        {
            public readonly string Id = Guid.NewGuid().ToString("N");
            public TrackInfo Track;
            public readonly ListeningCounter Counter = new ListeningCounter();
            public DateTimeOffset? Started;
            public double Position;
            public bool StartedPlaying, Playing, Ended, Stored;
            public int Source;
        }

        private readonly object state = new object();
        private readonly SemaphoreSlim storageGate = new SemaphoreSlim(1, 1);
        private readonly Func<PhoneListeningObservation> observation;
        private readonly Func<double> clock;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<PhoneReportingProfile, SubsonicServiceConfiguration> credentials;
        private readonly IPhoneListeningStore store;
        private readonly HttpClient http = new HttpClient();
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly List<Occurrence> occurrences = new List<Occurrence>();
        private readonly Dictionary<string, SubsonicServiceConfiguration> captured = new Dictionary<string, SubsonicServiceConfiguration>();
        private List<PhoneListenSubmission> pending;
        private Occurrence current, presence;
        private ThreadPoolTimer timer;
        private Task pumpingTask = Task.FromResult(true);
        private bool pumping, again;
        private volatile bool closing;
        private double lastPresence = double.NegativeInfinity;
        private double storageRetry;
        private readonly Dictionary<string, double> presenceRetries = new Dictionary<string, double>();
        private readonly Dictionary<string, double> submissionRetries = new Dictionary<string, double>();

        public PhoneListeningReporter(MediaPlayer player)
            : this(() => new PhoneListeningObservation
            {
                Playing = player.CurrentState == MediaPlayerState.Playing,
                Position = player.Position.TotalSeconds, Duration = player.NaturalDuration.TotalSeconds
            }, () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, () => DateTimeOffset.UtcNow) { }

        internal PhoneListeningReporter(Func<PhoneListeningObservation> observation, Func<double> clock,
            Func<DateTimeOffset> utcNow, IPhoneListeningStore store = null,
            Func<PhoneReportingProfile, SubsonicServiceConfiguration> credentials = null)
        {
            this.observation = observation; this.clock = clock; this.utcNow = utcNow;
            this.store = store ?? new PhoneListeningFileStore();
            this.credentials = credentials ?? LoadCredentials;
        }

        public void Start()
        {
            timer = ThreadPoolTimer.CreatePeriodicTimer(ignored => Observe(), TimeSpan.FromSeconds(1));
            Schedule();
        }

        public void SelectTrack(TrackInfo track, int source = 0)
        {
            lock (state)
            {
                FinishCurrent(); current = null; presence = null; lastPresence = double.NegativeInfinity;
                if (!closing && track != null && !string.IsNullOrWhiteSpace(track.Id) && ValidProfile(track.Reporting))
                {
                    current = new Occurrence { Track = track, Source = source };
                    current.Counter.DurationSeconds = Math.Max(0, track.Duration);
                    current.Counter.Observe(clock(), false, 1);
                    occurrences.Add(current);
                }
            }
            Schedule();
        }

        // Only a native Playing state transition arms a newly selected source.
        // Timer samples cannot mistake the previous source for the new song.
        public void Observe(bool nativeStateChanged = false, int? source = null)
        {
            try
            {
                var sample = observation(); var now = clock();
                lock (state)
                {
                    if (closing) return;
                    if (source.HasValue && current != null && current.Source != source.Value) return;
                    if (current != null && !current.Ended)
                    {
                        if (nativeStateChanged && sample.Playing) current.StartedPlaying = true;
                        var playing = sample.Playing && current.StartedPlaying;
                        current.Playing = playing;
                        if (playing && !current.Started.HasValue)
                        {
                            current.Started = utcNow();
                            Configuration(current.Track.Reporting);
                        }
                        if (sample.Duration > 0 && !double.IsNaN(sample.Duration) && !double.IsInfinity(sample.Duration))
                            current.Counter.DurationSeconds = sample.Duration;
                        current.Position = double.IsNaN(sample.Position) || double.IsInfinity(sample.Position) ? 0 : Math.Max(0, sample.Position);
                        current.Counter.Observe(now, playing, 1);
                        if (playing && now - lastPresence >= 10) { presence = current; lastPresence = now; }
                        else if (!playing && ReferenceEquals(presence, current)) presence = null;
                    }
                }
                Schedule();
            }
            catch (Exception exception) { Log(exception); }
        }

        public void Finish(int? source = null)
        {
            lock (state)
            {
                if (source.HasValue && current != null && current.Source != source.Value) return;
                FinishCurrent(); presence = null;
            }
            Schedule();
        }

        private void FinishCurrent()
        {
            if (current == null || current.Ended) return;
            current.Counter.Observe(clock(), false, 1); current.Ended = true; current.Playing = false;
            occurrences.RemoveAll(item => item.Ended && (item.Stored || !item.Counter.Qualified));
        }

        private void Schedule()
        {
            lock (state)
            {
                if (closing) return;
                if (pumping) { again = true; return; }
                pumping = true; pumpingTask = PumpAsync();
            }
        }

        private async Task PumpAsync()
        {
            while (true)
            {
                try
                {
                    await SaveQualifiedAsync();
                    if (!closing) await SubmitAsync();
                    if (!closing) await ReportPresenceAsync();
                }
                catch (OperationCanceledException) { }
                catch (Exception exception) { Log(exception); }
                lock (state)
                {
                    if (again && !closing) { again = false; continue; }
                    pumping = false; return;
                }
            }
        }

        internal Task DrainAsync() { lock (state) return pumpingTask; }

        private async Task SaveQualifiedAsync()
        {
            if (!closing && clock() < storageRetry) return;
            await storageGate.WaitAsync();
            try
            {
                if (pending == null) pending = await store.ReadAsync();
                List<Occurrence> ready;
                List<PhoneListenSubmission> additions;
                lock (state)
                {
                    ready = occurrences.Where(item => item.Counter.Qualified && item.Started.HasValue && !item.Stored).ToList();
                    additions = ready.Select(item => new PhoneListenSubmission
                    {
                        Listen = new PendingListen { OccurrenceId = item.Id, SongId = item.Track.Id, StartedUtc = item.Started.Value.UtcDateTime },
                        Profile = item.Track.Reporting, Position = (int)Math.Min(int.MaxValue, item.Position)
                    }).ToList();
                }
                if (ready.Count == 0) return;
                var next = new List<PhoneListenSubmission>(pending);
                foreach (var addition in additions)
                    if (!next.Any(entry => entry.Listen.OccurrenceId == addition.Listen.OccurrenceId)) next.Add(addition);
                if (next.Count > PhoneListeningFileStore.MaximumEntries) throw new InvalidDataException("Listening retry queue is full.");
                await store.WriteAsync(next); pending = next;
                lock (state)
                {
                    foreach (var item in ready) item.Stored = true;
                    occurrences.RemoveAll(item => item.Ended && item.Stored);
                }
                storageRetry = 0;
            }
            catch (Exception exception) { storageRetry = clock() + 30; Log(exception); }
            finally { storageGate.Release(); }
        }

        private SubsonicServiceConfiguration Configuration(PhoneReportingProfile profile)
        {
            if (!ValidProfile(profile)) return null;
            lock (state)
            {
                var key = ProfileKey(profile);
                SubsonicServiceConfiguration configuration;
                if (captured.TryGetValue(key, out configuration)) return configuration;
                var loaded = credentials(profile);
                if (loaded == null) return null;
                if (AccountIdentity.For(loaded) != AccountIdentity.For(ProfileConfiguration(profile)) ||
                    loaded.AuthenticationMode.ToString() != profile.AuthenticationMode) return null;
                configuration = new SubsonicServiceConfiguration
                {
                    BaseUrl = loaded.BaseUrl, Username = loaded.Username, Password = loaded.Password,
                    AuthenticationMode = loaded.AuthenticationMode
                };
                captured[key] = configuration; return configuration;
            }
        }

        private async Task SubmitAsync()
        {
            if (pending == null) return;
            // Bound each pass so restored history cannot starve current presence.
            for (var count = 0; count < 4 && !closing; ++count)
            {
                PhoneListenSubmission entry = null; SubsonicServiceConfiguration configuration = null;
                foreach (var candidate in pending)
                {
                    double retry;
                    if (submissionRetries.TryGetValue(ProfileKey(candidate.Profile), out retry) && clock() < retry) continue;
                    configuration = Configuration(candidate.Profile);
                    if (configuration != null) { entry = candidate; break; }
                }
                if (entry == null) return;
                try
                {
                    await SendAsync(new NavidromeApi(configuration).Scrobble(entry.Listen.SongId, true,
                        new DateTimeOffset(DateTime.SpecifyKind(entry.Listen.StartedUtc, DateTimeKind.Utc)), entry.Position));
                    await storageGate.WaitAsync();
                    try
                    {
                        var next = pending.Where(item => item.Listen.OccurrenceId != entry.Listen.OccurrenceId).ToList();
                        await store.WriteAsync(next); pending = next;
                    }
                    finally { storageGate.Release(); }
                }
                catch (OperationCanceledException) { if (!closing) Retry(entry.Profile, submissionRetries); }
                catch (Exception exception) { Retry(entry.Profile, submissionRetries); Log(exception); }
            }
        }

        private async Task ReportPresenceAsync()
        {
            Occurrence item;
            DateTimeOffset started;
            int position;
            lock (state)
            {
                item = presence; presence = null;
                if (item == null || item.Ended || !item.Playing || !item.Started.HasValue) return;
                started = item.Started.Value; position = (int)Math.Min(int.MaxValue, item.Position);
            }
            double retry;
            if (presenceRetries.TryGetValue(ProfileKey(item.Track.Reporting), out retry) && clock() < retry) return;
            var configuration = Configuration(item.Track.Reporting);
            if (configuration == null) return;
            try
            {
                lock (state) if (!ReferenceEquals(current, item) || item.Ended || !item.Playing) return;
                await SendAsync(new NavidromeApi(configuration).Scrobble(item.Track.Id, false, started, position));
            }
            catch (OperationCanceledException) { if (!closing) Retry(item.Track.Reporting, presenceRetries); }
            catch (Exception exception) { Retry(item.Track.Reporting, presenceRetries); Log(exception); }
        }

        private static string ProfileKey(PhoneReportingProfile profile)
        { return profile.BaseUrl + "\n" + profile.Username + "\n" + profile.AuthenticationMode + "\n" + profile.CredentialResource; }

        private void Retry(PhoneReportingProfile profile, IDictionary<string, double> retries)
        {
            var key = ProfileKey(profile); retries[key] = clock() + 30;
            lock (state) captured.Remove(key);
        }

        private async Task SendAsync(NavidromeResult<bool> request)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using (var response = await http.GetAsync(new Uri(request.RequestUrl)).AsTask(timeout.Token))
                {
                    response.EnsureSuccessStatusCode();
                    var text = await response.Content.ReadAsStringAsync().AsTask(timeout.Token);
                    if (text.Length > 1024 * 1024) throw new InvalidDataException("Listening response is too large.");
                    using (var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                        request.HandleResponse(XDocument.Load(reader));
                }
            }
        }

        public async Task StopAsync()
        {
            lock (state) { if (closing) return; FinishCurrent(); closing = true; presence = null; }
            if (timer != null) timer.Cancel();
            shutdown.Cancel();
            await SaveQualifiedAsync();
            await DrainAsync();
            http.Dispose(); shutdown.Dispose(); captured.Clear();
        }

        internal static bool ValidProfile(PhoneReportingProfile profile)
        {
            Uri address; SubsonicAuthenticationMode mode;
            return profile != null && !string.IsNullOrWhiteSpace(profile.Username) && !string.IsNullOrWhiteSpace(profile.CredentialResource) &&
                Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out address) && (address.Scheme == "http" || address.Scheme == "https") &&
                string.IsNullOrEmpty(address.UserInfo) && string.IsNullOrEmpty(address.Query) && string.IsNullOrEmpty(address.Fragment) &&
                Enum.TryParse(profile.AuthenticationMode, out mode) && Enum.IsDefined(typeof(SubsonicAuthenticationMode), mode);
        }

        private static SubsonicServiceConfiguration ProfileConfiguration(PhoneReportingProfile profile)
        {
            SubsonicAuthenticationMode mode; Enum.TryParse(profile.AuthenticationMode, out mode);
            return new SubsonicServiceConfiguration { BaseUrl = profile.BaseUrl, Username = profile.Username, AuthenticationMode = mode };
        }

        internal static SubsonicServiceConfiguration LoadCredentials(PhoneReportingProfile profile)
        {
            if (!ValidProfile(profile)) return null;
            var values = ApplicationData.Current.LocalSettings.Values;
            object username, authentication;
            if (!values.TryGetValue("Username", out username) || (string)username != profile.Username) return null;
            values.TryGetValue("AuthenticationMode", out authentication);
            if ((authentication as string ?? SubsonicAuthenticationMode.Password.ToString()) != profile.AuthenticationMode) return null;
            var configuration = ProfileConfiguration(profile); var account = AccountIdentity.For(configuration);
            var matchingRoute = false;
            foreach (var key in new[] { "PrimaryUrl", "SecondaryUrl", "BaseUrl" })
            {
                object route;
                if (values.TryGetValue(key, out route) && route is string && !string.IsNullOrWhiteSpace((string)route))
                    matchingRoute |= AccountIdentity.For(new SubsonicServiceConfiguration { BaseUrl = (string)route, Username = profile.Username }) == account;
            }
            if (!matchingRoute) return null;
            var credential = new PasswordVault().RetrieveAll().FirstOrDefault(item => item.Resource == profile.CredentialResource && item.UserName == profile.Username);
            if (credential == null) return null;
            credential.RetrievePassword();
            if (string.IsNullOrEmpty(credential.Password)) return null;
            configuration.Password = credential.Password; return configuration;
        }

        private static void Log(Exception exception) { Debug.WriteLine("Phone listening report failed: " + exception.GetType().Name); }
    }

    [DataContract]
    internal sealed class PhoneListenSubmission
    {
        [DataMember] public PendingListen Listen { get; set; }
        [DataMember] public PhoneReportingProfile Profile { get; set; }
        [DataMember] public int Position { get; set; }
    }

    internal interface IPhoneListeningStore
    {
        Task<List<PhoneListenSubmission>> ReadAsync();
        Task WriteAsync(List<PhoneListenSubmission> entries);
    }

    internal sealed class PhoneListeningFileStore : IPhoneListeningStore
    {
        internal const int MaximumEntries = 4096;
        private const string FileName = "MixDromePendingListening.json";
        public async Task<List<PhoneListenSubmission>> ReadAsync()
        {
            StorageFile file;
            try { file = await ApplicationData.Current.LocalFolder.GetFileAsync(FileName); }
            catch (FileNotFoundException) { return new List<PhoneListenSubmission>(); }
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > 16 * 1024 * 1024) throw new InvalidDataException("Listening retry file is too large.");
            var entries = AudioJson.Deserialize<List<PhoneListenSubmission>>(await FileIO.ReadTextAsync(file));
            if (entries == null || entries.Count > MaximumEntries || entries.Any(item => item == null || item.Listen == null ||
                !PhoneListeningReporter.ValidProfile(item.Profile) || string.IsNullOrWhiteSpace(item.Listen.SongId) ||
                string.IsNullOrWhiteSpace(item.Listen.OccurrenceId) || item.Listen.StartedUtc < new DateTime(1970, 1, 1) || item.Position < 0) ||
                entries.Select(item => item.Listen.OccurrenceId).Distinct().Count() != entries.Count)
                throw new InvalidDataException("Listening retry records are invalid.");
            return entries;
        }

        public async Task WriteAsync(List<PhoneListenSubmission> entries)
        {
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                "MixDromeListeningTransfer-" + Guid.NewGuid().ToString("N") + ".json", CreationCollisionOption.FailIfExists);
            try
            {
                await FileIO.WriteTextAsync(file, AudioJson.Serialize(entries));
                await file.RenameAsync(FileName, NameCollisionOption.ReplaceExisting);
            }
            catch { await file.DeleteAsync(); throw; }
        }
    }
}
