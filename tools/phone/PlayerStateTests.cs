using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Mixdrome.Phone.Common;
using Mixdrome.Phone.Services;
using Mixdrome.Phone.Views;
using Windows.Foundation.Collections;
using Windows.Media.Playback;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// run-playback.ps1 inserts the production page's exact state/rendering/lifecycle
// methods into this managed shell. These controls observe visible values and
// messages; they do not certify native XAML, media decoding or device lifetime.
public static class PhonePlayerStateTests
{
    private static int assertions;
    private static void Check(bool condition, string message) { ++assertions; if (!condition) throw new Exception(message); }
    private static ValueSet State(string version, int sequence, string kind = BackgroundAudioMessages.Snapshot)
    {
        return new ValueSet
        {
            { kind, kind == BackgroundAudioMessages.Failure ? (object)"PlaybackFailed" : true },
            { BackgroundAudioMessages.Session, "session" }, { BackgroundAudioMessages.Sequence, sequence },
            { BackgroundAudioMessages.QueueVersion, version }, { BackgroundAudioMessages.CurrentIndex, 1 },
            { BackgroundAudioMessages.CanNext, true }, { BackgroundAudioMessages.Shuffle, true },
            { BackgroundAudioMessages.Repeat, 1 }, { BackgroundAudioMessages.PlayerState, "Stopped" }
        };
    }
    private static string Snapshot(string version, string title, string artwork = null)
    {
        return AudioJson.Serialize(new AudioQueueSnapshot { Version = version, Tracks = new List<TrackInfo>
        {
            new TrackInfo { Id = "duplicate", Title = "First " + title, Artist = "First Artist", Album = "Album" },
            new TrackInfo { Id = "duplicate", Title = title, Artist = "Selected Artist", Album = "Selected Album", CoverArt = artwork }
        } });
    }
    private static PlaybackPage Page()
    {
        BackgroundMediaPlayer.Messages.Clear();
        BackgroundMediaPlayer.Command = null;
        BackgroundMediaPlayer.Current.Reset();
        return new PlaybackPage();
    }
    public static int Main()
    {
        try { Run().GetAwaiter().GetResult(); Console.WriteLine("PASS: " + assertions + " production phone player failure/snapshot, sequence, version, metadata and departure assertions. Native XAML/audio remain unverified."); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private static async Task Run()
    {
        var page = Page();
        var pendingRead = new TaskCompletionSource<string>();
        FileIO.Read = () => pendingRead.Task;
        var oldSnapshot = page.Accept(State("queue", 1));
        Check(!oldSnapshot.IsCompleted, "An initial production snapshot is held at the actual awaited file read.");
        FileIO.Read = () => Task.FromResult(Snapshot("queue", "Failed track"));
        await page.Accept(State("queue", 2, BackgroundAudioMessages.Failure));
        Check(page.QueueItems.Count == 2 && page.SelectedIndex == -1, "Failure loads the queue without selecting an unrealized Pivot list.");
        Check(page.Title == "Failed track" && page.Artist == "Selected Artist" && page.Album == "Selected Album", "Failure displays the actual selected title, artist and album.");
        Check(page.Status == "PlaybackFailed" && page.Failed, "Loading a replacement snapshot cannot clear the current failure.");
        Check(page.PlayEnabled && page.NextEnabled, "Failure retains the real queue's transport controls.");
        Check(page.SelectionWrites == 0, "Metadata and transport can update before the queue view is loaded.");
        page.LoadQueue();
        Check(page.SelectedIndex == -1 && page.SelectionWrites == 0, "A loaded queue with an empty Items binding does not receive an out-of-range index.");
        page.PublishUnrelatedDuplicates();
        Check(page.SelectedIndex == -1 && page.SelectionWrites == 0, "Matching media IDs in a stale Items collection cannot select the wrong occurrence.");
        page.PublishQueue();
        Check(page.SelectedIndex == 1 && ReferenceEquals(page.SelectedQueueObject, page.QueueItems[1]), "VectorChanged selects the exact second duplicate occurrence once the Items binding is ready.");
        pendingRead.SetResult(Snapshot("queue", "Older title")); await oldSnapshot;
        Check(page.Title == "Failed track" && page.Failed, "The interrupted older snapshot cannot overwrite metadata or clear the newer failure.");
        Check(BackgroundMediaPlayer.Messages.Count == 0, "Displaying failure never replays, clears or replaces audio to hide the error.");
        var selectionWrites = page.SelectionWrites;
        page.UnloadQueue(); page.PublishQueue();
        Check(page.SelectionWrites == selectionWrites, "An unloaded queue view is detached and cannot receive selection writes.");
        page.LoadQueue();
        Check(ReferenceEquals(page.SelectedQueueObject, page.QueueItems[1]), "Loading the queue again synchronizes its retained duplicate occurrence.");

        await page.Accept(State("queue", 1, BackgroundAudioMessages.TrackChanged));
        Check(page.Title == "Failed track" && page.Failed, "An out-of-order older track change cannot clear a newer failure.");
        FileIO.Read = () => Task.FromResult(Snapshot("next", "Next track"));
        await page.Accept(State("next", 3, BackgroundAudioMessages.TrackChanged));
        Check(page.Title == "Next track" && !page.Failed && page.Status == string.Empty, "A genuinely newer track change can replace metadata and clear the prior failure.");

        page = Page(); pendingRead = new TaskCompletionSource<string>(); FileIO.Read = () => pendingRead.Task;
        oldSnapshot = page.Accept(State("old", 1));
        FileIO.Read = () => Task.FromResult(Snapshot("new", "Newest track"));
        await page.Accept(State("new", 2));
        pendingRead.SetResult(Snapshot("old", "Stale track")); await oldSnapshot;
        Check(page.Title == "Newest track" && page.QueueItems.Count == 2, "A late read from an older queue version cannot replace the current queue.");

        page = Page(); FileIO.Read = () => Task.FromResult(Snapshot("actual", "Actual track"));
        await page.Accept(State("expected", 1, BackgroundAudioMessages.Failure));
        Check(page.QueueItems.Count == 0 && page.Title == "PlaybackEmpty", "A mismatched file version is not committed under a failed message's queue identity.");
        Check(BackgroundMediaPlayer.Messages.Count == 1 && BackgroundMediaPlayer.Messages[0].ContainsKey(BackgroundAudioMessages.RequestState), "A mismatched file requests authoritative state instead of guessing the queue.");
        await page.Accept(State("actual", 2, BackgroundAudioMessages.Failure));
        Check(page.Title == "Actual track" && page.Failed, "A matching authoritative failure restores metadata without losing the error.");

        page = Page(); pendingRead = new TaskCompletionSource<string>(); FileIO.Read = () => pendingRead.Task;
        var leavingFailure = page.Accept(State("queue", 1, BackgroundAudioMessages.Failure));
        page.Leave(); pendingRead.SetResult(Snapshot("queue", "Late track")); await leavingFailure;
        Check(page.QueueItems.Count == 0 && page.Title == "PlaybackEmpty" && page.Status == string.Empty, "Leaving during a failure snapshot prevents late metadata and error UI updates.");
        Check(BackgroundMediaPlayer.Messages.Count == 0, "An abandoned page cannot issue playback or recovery commands after its file read completes.");

        page = Page(); FileIO.Read = () => Task.FromResult(Snapshot("queue", "Leaving loaded queue"));
        await page.Accept(State("queue", 1)); page.LoadQueue();
        page.Leave(); page.PublishQueue();
        Check(page.SelectionWrites == 0, "Leaving a loaded page detaches Items changes before the binding can finish.");

        page = Page(); FileIO.Read = () => Task.FromResult(Snapshot("webp", "Actual failed track", "https://example.invalid/artwork.webp"));
        await page.Accept(State("webp", 1, BackgroundAudioMessages.Failure));
        var unsupportedImage = page.Artwork;
        Check(unsupportedImage.FailureSubscribers == 1, "A non-default cover subscribes to the public BitmapImage.ImageFailed event.");
        unsupportedImage.Fail();
        var defaultImage = page.Artwork;
        Check(defaultImage.UriSource.AbsoluteUri == "ms-appx:///Assets/MixRadio/DefaultArtwork300.png" && ReferenceEquals(defaultImage, page.BackgroundArtwork), "An unsupported image falls back to the existing packaged PNG in the current cover and background.");
        Check(page.Title == "Actual failed track" && page.QueueItems.Count == 2 && page.Failed && page.Status == "PlaybackFailed", "Image fallback preserves queue metadata and the existing audio failure state.");
        Check(unsupportedImage.FailureSubscribers == 0 && defaultImage.FailureSubscribers == 0, "The failed bitmap is detached and the default PNG has no failure subscription or retry loop.");
        defaultImage.Fail();
        Check(ReferenceEquals(defaultImage, page.Artwork) && BackgroundMediaPlayer.Messages.Count == 0, "Default image failure cannot replay audio, alter the queue or loop through image requests.");

        FileIO.Read = () => Task.FromResult(Snapshot("real", "Real cover", "https://example.invalid/real-cover.png"));
        await page.Accept(State("real", 2));
        var realImage = page.Artwork;
        Check(realImage.UriSource.AbsoluteUri == "https://example.invalid/real-cover.png" && ReferenceEquals(realImage, page.BackgroundArtwork), "A real cover remains the displayed source without an image failure.");
        FileIO.Read = () => Task.FromResult(Snapshot("newest", "Newest cover", "https://example.invalid/newest-cover.png"));
        await page.Accept(State("newest", 3));
        var newestImage = page.Artwork;
        page.DeliverImageFailure(realImage);
        Check(realImage.FailureSubscribers == 0 && ReferenceEquals(newestImage, page.Artwork), "A queued failure callback from an old bitmap cannot replace a newer cover.");
        newestImage.Fail();
        Check(!page.Failed && page.Status == string.Empty && page.Title == "Newest cover" && BackgroundMediaPlayer.Messages.Count == 0, "Image decode failure does not invent an audio failure or restart playback.");

        FileIO.Read = () => Task.FromResult(Snapshot("leaving", "Leaving cover", "https://example.invalid/leaving-cover.png"));
        await page.Accept(State("leaving", 4));
        var leavingImage = page.Artwork; page.Leave();
        page.DeliverImageFailure(leavingImage);
        Check(leavingImage.FailureSubscribers == 0 && ReferenceEquals(leavingImage, page.Artwork), "Leaving detaches the image event and ignores a previously queued failure callback.");

        page = Page(); FileIO.Read = () => Task.FromResult(Snapshot("reset", "Reset cover", "https://example.invalid/reset-cover.png"));
        await page.Accept(State("reset", 1));
        var resetImage = page.Artwork; page.ResetView(); page.DeliverImageFailure(resetImage);
        Check(resetImage.FailureSubscribers == 0 && page.Title == "PlaybackEmpty" && ReferenceEquals(resetImage, page.Artwork), "ResetDisplay detaches old artwork and ignores its late callbacks.");

        page = Page();
        var native = BackgroundMediaPlayer.Current;
        native.NaturalDuration = TimeSpan.FromSeconds(90); native.Position = TimeSpan.FromSeconds(17); native.CanSeek = true;
        foreach (var state in new[] { MediaPlayerState.Closed, MediaPlayerState.Opening, MediaPlayerState.Buffering })
        {
            native.CurrentState = state; native.ClearReadCounts(); page.Poll();
            Check(native.ProgressReads == 0, "Closed/Opening/Buffering polling never touches native duration, position or seek capabilities before they are ready.");
            Check(!page.SeekEnabled && page.Progress == 0 && page.Elapsed == "0:00" && page.Duration == "0:00", "Unready media exposes disabled zero progress without reporting a playback failure.");
        }
        native.CurrentState = MediaPlayerState.Playing; page.Poll();
        Check(page.SeekEnabled && page.Progress == 17 && page.Elapsed == "0:17" && page.Duration == "1:30", "Playing media supplies its actual native position and duration.");
        native.CurrentState = MediaPlayerState.Paused; native.Position = TimeSpan.FromSeconds(73); page.Poll();
        Check(page.Progress == 73 && page.Elapsed == "1:13", "Paused media continues exposing its real position.");

        native.CurrentState = MediaPlayerState.Playing;
        native.DurationRead = () => { native.CurrentState = MediaPlayerState.Opening; throw new COMException("Source changed before the duration read.", unchecked((int)0x80004005)); };
        page.Poll();
        Check(!page.SeekEnabled && page.Progress == 0 && !page.Failed, "E_FAIL during a confirmed transition to Opening resets progress without inventing a media failure.");
        native.CurrentState = MediaPlayerState.Playing;
        native.DurationRead = () => { throw new COMException("Failure while the native source remains ready.", unchecked((int)0x80004005)); };
        var propagated = false;
        try { page.Poll(); } catch (COMException) { propagated = true; }
        Check(propagated, "E_FAIL while the player remains ready is not hidden as a transient state.");
        native.CurrentState = MediaPlayerState.Playing;
        native.DurationRead = () => { native.CurrentState = MediaPlayerState.Opening; throw new COMException("An unrelated native error.", unchecked((int)0x80070057)); };
        propagated = false;
        try { page.Poll(); } catch (COMException) { propagated = true; }
        Check(propagated, "An unrelated COM error is not swallowed even when source state changes.");

        page = Page(); native = BackgroundMediaPlayer.Current; native.CurrentState = MediaPlayerState.Closed;
        BackgroundMediaPlayer.Command = message =>
        {
            if (!message.ContainsKey(BackgroundAudioMessages.RequestState)) return;
            var readyState = State(string.Empty, 1, BackgroundAudioMessages.Ready);
            readyState[BackgroundAudioMessages.CurrentIndex] = -1;
            page.Accept(readyState).GetAwaiter().GetResult();
            native.CurrentState = MediaPlayerState.Playing;
            native.DurationRead = () => { throw new COMException("Initial ready-state update failed.", unchecked((int)0x80004005)); };
        };
        page.ActivateBack();
        Check(page.TimerRunning && page.Status == "PlaybackFailed", "An activation starts progress polling after readiness even if its initial native state update fails.");
        Check(page.QueueStartCalls == 0, "Reconnecting through Back never starts the navigation queue again.");
        native.DurationRead = null; native.NaturalDuration = TimeSpan.FromSeconds(90); native.Position = TimeSpan.FromSeconds(41); native.CanSeek = true;
        page.Poll();
        Check(page.TimerRunning && page.Progress == 41 && page.Duration == "1:30", "Later timer ticks recover real progress after an initial activation read error.");
        page.Leave();
        Check(!page.TimerRunning, "Leaving the page stops polling even after activation recovery.");
    }
}

namespace Mixdrome.Phone.Views
{
    public sealed class PlaybackPage : Page
    {
        private const string DefaultArtworkUri = "ms-appx:///Assets/MixRadio/DefaultArtwork300.png";
        private readonly DispatcherTimer progressTimer = new DispatcherTimer();
        private readonly AudioStateVersionGate stateGate = new AudioStateVersionGate();
        private TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>();
        private CancellationTokenSource activation = new CancellationTokenSource();
        private bool active = true, updatingProgress, seeking, shuffle, canNext, playbackFailed;
        private int repeat, currentIndex = -1;
        private string queueVersion = string.Empty, latestVersion = string.Empty;
        private MediaPlayer player = BackgroundMediaPlayer.Current;
        private TrackInfo displayedTrack;
        private BitmapImage artworkImage;
        private ItemCollection observedQueueItems;
        private bool queueListLoaded;
        public ObservableCollection<TrackInfo> QueueItems { get; private set; } = new ObservableCollection<TrackInfo>();
        private readonly TextBlock SongTitleText = new TextBlock(), AboutTitleText = new TextBlock(), ArtistText = new TextBlock(), AboutArtistText = new TextBlock(), AboutAlbumText = new TextBlock(), PlaybackStatusText = new TextBlock(), PlayPauseGlyph = new TextBlock(), CurrentTimeText = new TextBlock(), TotalTimeText = new TextBlock();
        private readonly Control PreviousButton = new Control(), PlayPauseButton = new Control(), NextButton = new Control(), ShuffleButton = new Control(), RepeatButton = new Control(), RepeatOneBadge = new Control();
        private readonly Slider ProgressSlider = new Slider();
        private readonly ListView QueueListView = new ListView();
        private readonly Image AlbumArtImage = new Image(), BackgroundImage = new Image();
        public PlaybackPage() { ResetDisplay(); }
        public Task Accept(ValueSet data) { return ApplyStateAsync(data); }
        public void Leave() { OnNavigatedFrom(new NavigationEventArgs()); }
        public void ActivateBack() { OnNavigatedTo(new NavigationEventArgs { NavigationMode = NavigationMode.Back }); }
        public void Poll() { OnProgressTimerTick(null, null); }
        public void ResetView() { ResetDisplay(); }
        public void DeliverImageFailure(BitmapImage image) { OnArtworkImageFailed(image, new ExceptionRoutedEventArgs()); }
        public void LoadQueue() { QueueListView.IsLoaded = true; OnQueueListLoaded(QueueListView, new RoutedEventArgs()); }
        public void UnloadQueue() { OnQueueListUnloaded(QueueListView, new RoutedEventArgs()); QueueListView.IsLoaded = false; }
        public void PublishQueue() { QueueListView.Items.Replace(QueueItems); }
        public void PublishUnrelatedDuplicates() { QueueListView.Items.Replace(new[] { new TrackInfo { Id = "duplicate" }, new TrackInfo { Id = "duplicate" } }); }
        public string Title { get { return SongTitleText.Text; } }
        public string Artist { get { return ArtistText.Text; } }
        public string Album { get { return AboutAlbumText.Text; } }
        public string Status { get { return PlaybackStatusText.Text; } }
        public bool Failed { get { return playbackFailed; } }
        public int SelectedIndex { get { return QueueListView.SelectedIndex; } }
        public int SelectionWrites { get { return QueueListView.SelectionWrites; } }
        public object SelectedQueueObject { get { return SelectedIndex >= 0 && SelectedIndex < QueueListView.Items.Count ? QueueListView.Items[SelectedIndex] : null; } }
        public bool PlayEnabled { get { return PlayPauseButton.IsEnabled; } }
        public bool NextEnabled { get { return NextButton.IsEnabled; } }
        public bool SeekEnabled { get { return ProgressSlider.IsEnabled; } }
        public double Progress { get { return ProgressSlider.Value; } }
        public string Elapsed { get { return CurrentTimeText.Text; } }
        public string Duration { get { return TotalTimeText.Text; } }
        public bool TimerRunning { get { return progressTimer.IsEnabled; } }
        public BitmapImage Artwork { get { return AlbumArtImage.Source; } }
        public BitmapImage BackgroundArtwork { get { return BackgroundImage.Source; } }
        public int QueueStartCalls;
        private Task StartQueueAsync(PlaybackNavigationParameter parameter, CancellationToken token) { ++QueueStartCalls; return Task.FromResult(true); }
        private void OnBackPressed(object sender, BackPressedEventArgs args) { }
        private void OnMessage(object sender, MediaPlayerDataReceivedEventArgs args) { }
        private void OnMediaStateChanged(MediaPlayer sender, object args) { }
/* PRODUCTION_PAGE_METHODS */
    }
    public sealed class PlaybackNavigationParameter { public List<SongDisplayItem> Playlist; public int StartIndex; }
    public sealed class SongDisplayItem { public string Id; }
}
namespace Mixdrome.Phone.Common { public static class PhoneText { public static string Get(string key) { return key; } } }
namespace Windows.Foundation.Collections
{
    public sealed class ValueSet : Dictionary<string, object> { }
    public interface IObservableVector<T> { }
    public interface IVectorChangedEventArgs { }
    public delegate void VectorChangedEventHandler<T>(IObservableVector<T> sender, IVectorChangedEventArgs args);
}
namespace Windows.Storage
{
    public sealed class StorageFile { }
    public sealed class StorageFolder { public Task<StorageFile> GetFileAsync(string name) { return Task.FromResult(new StorageFile()); } }
    public sealed class ApplicationData { public static readonly ApplicationData Current = new ApplicationData(); public readonly StorageFolder LocalFolder = new StorageFolder(); }
    public static class FileIO { public static Func<Task<string>> Read; public static Task<string> ReadTextAsync(StorageFile file) { return Read(); } }
}
namespace Windows.Media.Playback
{
    public enum MediaPlayerState { Playing, Paused, Stopped, Closed, Opening, Buffering }
    public sealed class MediaPlayer
    {
        public MediaPlayerState CurrentState;
        public Func<TimeSpan> DurationRead;
        private TimeSpan duration, position;
        private bool canSeek;
        public int ProgressReads;
        private void ReadProgress()
        {
            ++ProgressReads;
            if (CurrentState == MediaPlayerState.Closed || CurrentState == MediaPlayerState.Opening || CurrentState == MediaPlayerState.Buffering)
                throw new COMException("Native media properties are not initialized.", unchecked((int)0x80004005));
        }
        public TimeSpan NaturalDuration { get { ReadProgress(); return DurationRead == null ? duration : DurationRead(); } set { duration = value; } }
        public TimeSpan Position { get { ReadProgress(); return position; } set { position = value; } }
        public bool CanSeek { get { ReadProgress(); return canSeek; } set { canSeek = value; } }
        public void ClearReadCounts() { ProgressReads = 0; }
        public void Reset() { CurrentState = MediaPlayerState.Stopped; DurationRead = null; duration = position = TimeSpan.Zero; canSeek = false; ClearReadCounts(); }
        public event Action<MediaPlayer, object> CurrentStateChanged { add { } remove { } }
    }
    public sealed class MediaPlayerDataReceivedEventArgs : EventArgs { }
    public static class BackgroundMediaPlayer
    {
        public static readonly MediaPlayer Current = new MediaPlayer();
        public static readonly List<ValueSet> Messages = new List<ValueSet>();
        public static Action<ValueSet> Command;
        public static void SendMessageToBackground(ValueSet data) { Messages.Add(data); if (Command != null) Command(data); }
        public static event EventHandler<MediaPlayerDataReceivedEventArgs> MessageReceivedFromBackground { add { } remove { } }
    }
}
namespace Windows.Phone.UI.Input
{
    public sealed class BackPressedEventArgs : EventArgs { }
    public static class HardwareButtons { public static event EventHandler<BackPressedEventArgs> BackPressed { add { } remove { } } }
}
namespace Windows.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public sealed class RoutedEventArgs : EventArgs { }
    public sealed class ExceptionRoutedEventArgs : EventArgs { }
    public delegate void ExceptionRoutedEventHandler(object sender, ExceptionRoutedEventArgs args);
    public sealed class DispatcherTimer { public bool IsEnabled; public void Start() { IsEnabled = true; } public void Stop() { IsEnabled = false; } }
    public sealed class Application
    {
        public static readonly Application Current = new Application();
        public readonly Dictionary<string, object> Resources = new Dictionary<string, object> { { "MixRadioAccentBrush", new Media.Brush() }, { "ApplicationForegroundThemeBrush", new Media.Brush() } };
    }
}
namespace Windows.UI.Xaml.Controls
{
    public class Control { public bool IsEnabled; public Visibility Visibility; public Media.Brush Foreground; }
    public class Page { protected virtual void OnNavigatedTo(NavigationEventArgs args) { } protected virtual void OnNavigatedFrom(NavigationEventArgs args) { } }
    public sealed class TextBlock : Control { public string Text = string.Empty; }
    public sealed class Slider : Control { public double Maximum, Value; }
    public sealed class ItemCollection : List<object>, IObservableVector<object>
    {
        public event VectorChangedEventHandler<object> VectorChanged;
        public void Replace(IEnumerable<object> items)
        {
            Clear(); AddRange(items);
            if (VectorChanged != null) VectorChanged(this, null);
        }
    }
    public sealed class ListView : Control
    {
        public bool IsLoaded;
        public int SelectionWrites;
        public readonly ItemCollection Items = new ItemCollection();
        private int selectedIndex = -1;
        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                ++SelectionWrites;
                if (!IsLoaded || value < -1 || value >= Items.Count) throw new ArgumentException("The queue view is not ready for this selected index.");
                selectedIndex = value;
            }
        }
    }
    public sealed class Image { public BitmapImage Source; }
}
namespace Windows.UI.Xaml.Media { public class Brush { } }
namespace Windows.UI.Xaml.Media.Imaging
{
    public sealed class BitmapImage
    {
        public Uri UriSource;
        private ExceptionRoutedEventHandler failed;
        public BitmapImage() { }
        public BitmapImage(Uri uri) { UriSource = uri; }
        public event ExceptionRoutedEventHandler ImageFailed { add { failed += value; } remove { failed -= value; } }
        public int FailureSubscribers { get { return failed == null ? 0 : failed.GetInvocationList().Length; } }
        public void Fail() { if (failed != null) failed(this, new ExceptionRoutedEventArgs()); }
    }
}
namespace Windows.UI.Xaml.Automation { public static class AutomationProperties { public static void SetName(Control control, string name) { } } }
namespace Windows.UI.Xaml.Navigation
{
    public enum NavigationMode { New, Back }
    public sealed class NavigationEventArgs : EventArgs { public NavigationMode NavigationMode; public object Parameter; }
}
