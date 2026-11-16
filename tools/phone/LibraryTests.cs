using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Client.Common.Services.DataStructures.SubsonicService;
using Mixdrome.Phone.Services;
using Mixdrome.Phone.Views;
using Windows.Phone.UI.Input;
using Windows.Security.Credentials;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// Executes the production library page against delayed HTTP peers. Managed controls
// observe navigation/state only; they do not certify native rendering or touch input.
public static class PhoneLibraryTests
{
    private static int assertions;
    private static void Check(bool condition, string message) { ++assertions; if (!condition) throw new Exception(message); }
    private static object Call(LibraryPage page, string method, params object[] arguments)
    { return typeof(LibraryPage).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(page, arguments); }
    private static Task Open(LibraryPage page, PhoneLibraryItem item) { return (Task)Call(page, "OpenItem", item); }
    private static string Response(string body, bool success = true)
    { return "<subsonic-response xmlns='http://subsonic.org/restapi' status='" + (success ? "ok" : "failed") + "' version='1.16.1'>" + body + "</subsonic-response>"; }
    private static async Task WaitFor(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition()) { if (DateTime.UtcNow >= deadline) throw new Exception(message); await Task.Delay(10); }
    }
    private static async Task<LibraryPage> Page()
    {
        var page = new LibraryPage();
        Call(page, "OnNavigatedTo", new NavigationEventArgs { Parameter = "playlists" });
        await WaitFor(() => page.Playlists.Count == 4, "Library initialization did not complete.");
        return page;
    }
    private static PhoneLibraryItem Item(LibraryPage page, string id) { return page.Playlists.First(item => item.Id == id); }
    private static void Leave(LibraryPage page) { Call(page, "OnNavigatedFrom", new NavigationEventArgs()); }
    private static string StartedTrack(LibraryPage page)
    { return ((PlaybackNavigationParameter)page.Frame.Navigations.Single().Parameter).Playlist[0].Id; }
    public static int Main()
    {
        try { Run().GetAwaiter().GetResult(); Console.WriteLine("PASS: " + assertions + " production phone library delayed-selection, tab, refresh, departure and error assertions. Native rendering remains unverified."); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private static async Task Run()
    {
        ApplicationData.Current.LocalSettings.Values.Clear(); PasswordVault.Credentials.Clear();
        using (var server = new ProtocolServer(request =>
        {
            var path = request.Uri.AbsolutePath;
            if (path.EndsWith("getPlaylists.view")) return new ProtocolResponse { Body = Response("<playlists><playlist id='A' name='A' songCount='2' /><playlist id='B' name='B' songCount='2' /><playlist id='F' name='Failure' songCount='2' /><playlist id='E' name='Empty' songCount='0' /></playlists>") };
            if (path.EndsWith("getArtists.view")) return new ProtocolResponse { Body = Response("<artists><index name='A'><artist id='artist' name='Artist' albumCount='1' /></index></artists>") };
            if (path.EndsWith("getPlaylist.view"))
            {
                var id = request.Parameter("id");
                if (id == "F") return new ProtocolResponse { DelayMilliseconds = 200, Body = Response("<error code='40' message='Denied' />", false) };
                if (id == "E") return new ProtocolResponse { Body = Response("<playlist id='E' name='Empty' />") };
                return new ProtocolResponse { DelayMilliseconds = id == "A" ? 300 : 650,
                    Body = Response("<playlist id='" + id + "' name='" + id + "'><entry id='" + id + "-1' title='First' artist='Artist' album='Album' duration='90' /><entry id='" + id + "-2' title='Second' duration='95' /></playlist>") };
            }
            return new ProtocolResponse { Body = Response("") };
        }))
        {
            PhoneSettings.Save(new SubsonicServiceConfiguration { PrimaryUrl = server.Url, BaseUrl = server.Url, Username = "fixture", Password = "secret" });
            var page = await Page();
            var a = Open(page, Item(page, "A"));
            await WaitFor(() => server.Requests.Any(request => request.Parameter("id") == "A"), "First playlist request was not issued.");
            var b = Open(page, Item(page, "B"));
            await a;
            Check(page.Frame.Navigations.Count == 0, "A faster prior playlist response cannot start after a newer selection.");
            await b;
            Check(page.Frame.Navigations.Count == 1 && StartedTrack(page) == "B-1", "Only the latest playlist selection starts playback.");
            var parameter = (PlaybackNavigationParameter)page.Frame.Navigations.Single().Parameter;
            Check(parameter.StartIndex == 0 && parameter.Playlist.Count == 2 && parameter.Playlist[0].Album == "Album", "Successful playlist navigation retains its full queue and metadata.");
            Leave(page);

            page = await Page();
            var failing = Open(page, Item(page, "F"));
            b = Open(page, Item(page, "B"));
            await failing;
            Check(string.IsNullOrEmpty(page.TestStatus.Text), "An error from an older selection cannot replace current page status.");
            await b;
            Check(page.Frame.Navigations.Count == 1 && StartedTrack(page) == "B-1", "A stale failed playlist cannot suppress the newer successful selection.");
            Leave(page);

            page = await Page();
            a = Open(page, Item(page, "A"));
            page.TestPivot.SelectedIndex = 0;
            Call(page, "OnTabChanged", null, new SelectionChangedEventArgs());
            await a;
            Check(page.Frame.Navigations.Count == 0, "Changing tabs invalidates pending playlist navigation.");
            Check(!page.TestStatus.Text.StartsWith("LoadFailed"), "A discarded playlist response does not affect the new tab status.");
            var beforeHidden = server.Requests.Count(request => request.Uri.AbsolutePath.EndsWith("getPlaylist.view"));
            await Open(page, Item(page, "B"));
            Check(server.Requests.Count(request => request.Uri.AbsolutePath.EndsWith("getPlaylist.view")) == beforeHidden,
                "An old playlist item click from a hidden tab cannot issue a playback request.");
            Leave(page);

            page = await Page();
            a = Open(page, Item(page, "A"));
            await (Task)Call(page, "Initialize");
            await a;
            Check(page.Frame.Navigations.Count == 0 && page.Playlists.Count == 4, "Refreshing discards pending playback without losing the refreshed library.");
            Leave(page);

            page = await Page();
            a = Open(page, Item(page, "A"));
            Leave(page);
            await a;
            Check(page.Frame.Navigations.Count == 0, "Leaving discards a late playlist completion.");

            page = await Page();
            await Open(page, Item(page, "F"));
            Check(page.Frame.Navigations.Count == 0 && page.TestStatus.Text.StartsWith("LoadFailed"), "The current failed selection reports a controlled error and does not navigate.");
            await Open(page, Item(page, "E"));
            Check(page.Frame.Navigations.Count == 0 && page.TestStatus.Text.StartsWith("PlaylistEmpty"), "The current empty playlist reports empty state without starting playback.");
            var handled = new BackPressedEventArgs { Handled = true };
            Call(page, "OnBackPressed", null, handled);
            Check(page.Frame.BackCount == 0, "An already handled Back event does not navigate twice.");
            Leave(page);
        }
    }
}

namespace Windows.UI.Xaml
{
    public enum Visibility { Visible, Collapsed }
    public class RoutedEventArgs : EventArgs { }
}
namespace Windows.UI.Xaml.Controls
{
    public class Control { public Visibility Visibility = Visibility.Visible; public bool IsEnabled = true; }
    public class Page : Control
    {
        public object DataContext;
        public readonly Frame Frame = new Frame();
        protected virtual void OnNavigatedTo(NavigationEventArgs e) { }
        protected virtual void OnNavigatedFrom(NavigationEventArgs e) { }
    }
    public sealed class NavigationRecord { public Type Type; public object Parameter; }
    public sealed class Frame
    {
        public bool CanGoBack = true; public int BackCount;
        public readonly List<NavigationRecord> Navigations = new List<NavigationRecord>();
        public bool Navigate(Type type, object parameter = null) { Navigations.Add(new NavigationRecord { Type = type, Parameter = parameter }); return true; }
        public void GoBack() { ++BackCount; }
    }
    public sealed class TextBlock : Control { public string Text = string.Empty; }
    public sealed class ProgressRing : Control { public bool IsActive; }
    public sealed class PivotItem : Control { public string Tag; }
    public sealed class Pivot : Control
    {
        public int SelectedIndex;
        public readonly List<PivotItem> Items = new List<PivotItem>();
        public object SelectedItem { get { return Items[SelectedIndex]; } }
    }
    public sealed class AppBarButton : Control { }
    public sealed class SelectionChangedEventArgs : EventArgs { }
    public sealed class ItemClickEventArgs : EventArgs { public object ClickedItem; }
}
namespace Windows.UI.Xaml.Navigation { public sealed class NavigationEventArgs : EventArgs { public object Parameter; } }
namespace Windows.Phone.UI.Input
{
    public sealed class BackPressedEventArgs : EventArgs { public bool Handled; }
    public static class HardwareButtons { public static event EventHandler<BackPressedEventArgs> BackPressed { add { } remove { } } }
}
namespace Mixdrome.Phone.Views
{
    public sealed partial class LibraryPage
    {
        private readonly Pivot LibraryPivot = new Pivot();
        private readonly TextBlock StatusText = new TextBlock();
        private readonly ProgressRing LoadingIndicator = new ProgressRing();
        private readonly AppBarButton MoreButton = new AppBarButton();
        public Pivot TestPivot { get { return LibraryPivot; } }
        public TextBlock TestStatus { get { return StatusText; } }
        private void InitializeComponent() { foreach (var tab in new[] { "artists", "albums", "songs", "genres", "playlists" }) LibraryPivot.Items.Add(new PivotItem { Tag = tab }); }
    }
    public sealed class ArtistAlbumsPage { }
    public sealed class AlbumPage { }
    public sealed class GenreSongsPage { }
    public sealed class PlaybackPage { }
    public sealed class SearchPage { }
    public sealed class PlaybackNavigationParameter { public List<SongDisplayItem> Playlist; public int StartIndex; }
    public sealed class ArtistDisplayItem { public string Id, Name; }
    public sealed class SongDisplayItem { public string Id, Title, Artist, Album, CoverArt; public int Track, Duration; }
}
