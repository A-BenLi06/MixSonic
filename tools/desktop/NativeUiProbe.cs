namespace Subsonic8.Diagnostics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.InteropServices.WindowsRuntime;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Subsonic8.Main;
    using Subsonic8.Navidrome;
    using Subsonic8.Shell;
    using Windows.Data.Json;
    using Windows.Foundation;
    using Windows.Graphics.Imaging;
    using Windows.Storage;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media;
    using Windows.UI.Xaml.Media.Imaging;

    // Included only in an explicit NativeUiProbe build. Screenshots come from the
    // running app's actual XAML tree; normal builds contain no capture/navigation driver.
    internal static class NativeUiProbe
    {
        private static bool _fatal;
        internal static void RecordFatalAndExit(string message, Exception exception)
        {
            if (_fatal) return;
            _fatal = true;
            try
            {
                var text = DateTimeOffset.Now.ToString("O") + Environment.NewLine + message + Environment.NewLine + exception;
                Task.Run(async () =>
                {
                    var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("native-ui-fatal.txt", CreationCollisionOption.ReplaceExisting);
                    await FileIO.WriteTextAsync(file, text);
                }).GetAwaiter().GetResult();
            }
            finally { Application.Current.Exit(); }
        }

        internal static async Task RunAsync(ShellView shell, INavigationService navigation)
        {
            try
            {
                var root = await ApplicationData.Current.LocalFolder.CreateFolderAsync("native-ui-probe", CreationCollisionOption.OpenIfExists);
                var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
                var folder = await root.CreateFolderAsync(stamp, CreationCollisionOption.FailIfExists);
                var report = new JsonObject();
                report["createdAt"] = JsonValue.CreateStringValue(DateTimeOffset.Now.ToString("O"));
                report["directory"] = JsonValue.CreateStringValue(folder.Path);
                var pages = new JsonArray();
                report["pages"] = pages;
                pages.Add(await Capture(shell, folder, "home-startup"));
                await SaveReport(root, folder, report);
                if (shell.ShellFrame.Content is MainView)
                {
                    if (NavigationExtensions.NavigateToViewModel<NavidromeBrowserViewModel>(navigation, "albums"))
                        pages.Add(await Capture(shell, folder, "albums"));
                    await SaveReport(root, folder, report);
                    if (NavigationExtensions.NavigateToViewModel<NavidromePlayerViewModel>(navigation))
                        pages.Add(await Capture(shell, folder, "player"));
                    await SaveReport(root, folder, report);
                    if (NavigationExtensions.NavigateToViewModel<MainViewModel>(navigation))
                        pages.Add(await Capture(shell, folder, "home"));
                    await SaveReport(root, folder, report);
                    var homeView = shell.ShellFrame.Content as MainView;
                    var hub = homeView == null ? null : homeView.FindName("MainHub") as Hub;
                    if (hub != null)
                    {
                        hub.ScrollToSection(hub.Sections[1]);
                        pages.Add(await Capture(shell, folder, "home-library"));
                        await SaveReport(root, folder, report);
                        hub.ScrollToSection(hub.Sections[0]);
                    }
                    var width = shell.Width;
                    var height = shell.Height;
                    try
                    {
                        shell.Width = 500;
                        shell.Height = 900;
                        shell.UpdateLayout();
                        pages.Add(await Capture(shell, folder, "home-narrow"));
                    }
                    finally { shell.Width = width; shell.Height = height; shell.UpdateLayout(); }
                }
                await SaveReport(root, folder, report);
                App.LogStartupEvent("Native UI probe complete: " + folder.Path);
            }
            catch (Exception exception) { App.LogStartupException("Native UI probe failed", exception); }
        }

        private static async Task SaveReport(StorageFolder root, StorageFolder folder, JsonObject report)
        {
            var text = report.Stringify();
            await FileIO.WriteTextAsync(await folder.CreateFileAsync("result.json", CreationCollisionOption.ReplaceExisting), text);
            await FileIO.WriteTextAsync(await root.CreateFileAsync("latest.json", CreationCollisionOption.ReplaceExisting), text);
        }

        private static async Task<JsonObject> Capture(ShellView shell, StorageFolder folder, string name)
        {
            var page = shell.ShellFrame.Content as FrameworkElement;
            var model = page == null ? null : page.DataContext as NavidromePageViewModel;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
            while (model != null && model.IsLoading && DateTimeOffset.UtcNow < deadline) await Task.Delay(100);
            await Task.Delay(500);
            shell.UpdateLayout();
            var artworkDeadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (RemoteImages(shell).Any(bitmap => bitmap.PixelWidth == 0) && DateTimeOffset.UtcNow < artworkDeadline)
                await Task.Delay(100);
            // Include the recovered 600 ms fade after the last image finishes decoding.
            await Task.Delay(700);
            var result = new JsonObject();
            result["name"] = JsonValue.CreateStringValue(name);
            result["page"] = JsonValue.CreateStringValue(page == null ? string.Empty : page.GetType().FullName);
            result["loading"] = JsonValue.CreateBooleanValue(model != null && model.IsLoading);
            result["hasError"] = JsonValue.CreateBooleanValue(model != null && model.HasError);
            result["width"] = JsonValue.CreateNumberValue(shell.ActualWidth);
            result["height"] = JsonValue.CreateNumberValue(shell.ActualHeight);
            result["initializing"] = JsonValue.CreateBooleanValue(((ShellViewModel)shell.DataContext).IsInitializing);
            result["artworkDecoded"] = JsonValue.CreateNumberValue(RemoteImages(shell).Count(bitmap => bitmap.PixelWidth > 0));
            result["artworkPendingOrUnavailable"] = JsonValue.CreateNumberValue(RemoteImages(shell).Count(bitmap => bitmap.PixelWidth == 0));
            var lists = new JsonArray();
            foreach (var list in Descendants(shell).OfType<GridView>().Where(list => list.Visibility == Visibility.Visible))
            {
                var layout = new JsonObject();
                layout["name"] = JsonValue.CreateStringValue(list.Name);
                layout["width"] = JsonValue.CreateNumberValue(list.ActualWidth);
                layout["height"] = JsonValue.CreateNumberValue(list.ActualHeight);
                layout["padding"] = JsonValue.CreateStringValue(list.Padding.ToString());
                layout["count"] = JsonValue.CreateNumberValue(list.Items.Count);
                var panel = list.ItemsPanelRoot;
                if (panel != null)
                {
                    layout["panelWidth"] = JsonValue.CreateNumberValue(panel.ActualWidth);
                    layout["panelHeight"] = JsonValue.CreateNumberValue(panel.ActualHeight);
                }
                var bounds = new JsonArray();
                for (var index = 0; index < list.Items.Count; index++)
                {
                    var item = list.ContainerFromIndex(index) as FrameworkElement;
                    if (item == null) continue;
                    var point = item.TransformToVisual(list).TransformPoint(new Point());
                    var box = new JsonObject();
                    box["index"] = JsonValue.CreateNumberValue(index);
                    box["x"] = JsonValue.CreateNumberValue(point.X);
                    box["y"] = JsonValue.CreateNumberValue(point.Y);
                    box["width"] = JsonValue.CreateNumberValue(item.ActualWidth);
                    box["height"] = JsonValue.CreateNumberValue(item.ActualHeight);
                    bounds.Add(box);
                }
                layout["items"] = bounds;
                lists.Add(layout);
            }
            result["lists"] = lists;
            var home = model as MainViewModel;
            if (home != null)
            {
                var sections = new JsonArray();
                foreach (var section in new[] { home.Recent, home.Playlists, home.Artists, home.Downloads, home.Favourites, home.MyMusic })
                {
                    var item = new JsonObject();
                    item["route"] = JsonValue.CreateStringValue(section.Destination.Route);
                    item["count"] = JsonValue.CreateNumberValue(section.Items.Count);
                    item["loading"] = JsonValue.CreateBooleanValue(section.IsLoading);
                    item["hasError"] = JsonValue.CreateBooleanValue(!string.IsNullOrEmpty(section.Error));
                    sections.Add(item);
                }
                result["sections"] = sections;
            }
            try
            {
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(shell);
                var pixels = await bitmap.GetPixelsAsync();
                var filename = name + ".png";
                var file = await folder.CreateFileAsync(filename, CreationCollisionOption.FailIfExists);
                using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                        (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
                    await encoder.FlushAsync();
                }
                result["screenshot"] = JsonValue.CreateStringValue(filename);
            }
            catch (Exception exception) { result["captureError"] = JsonValue.CreateStringValue(exception.ToString()); }
            return result;
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        private static IEnumerable<BitmapImage> RemoteImages(DependencyObject root)
        {
            return Descendants(root).OfType<Image>().Select(control => control.Source).OfType<BitmapImage>()
                .Where(bitmap => bitmap.UriSource != null &&
                    (bitmap.UriSource.Scheme == "http" || bitmap.UriSource.Scheme == "https"));
        }
    }
}
