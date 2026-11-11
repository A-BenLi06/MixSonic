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
    using Windows.UI.Xaml.Automation.Peers;
    using Windows.UI.Xaml.Automation.Provider;
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
                    {
                        pages.Add(await Capture(shell, folder, "albums"));
                        var originalWidth = shell.Width;
                        var originalHeight = shell.Height;
                        try
                        {
                            // Match the app-content viewport in the supplied MixRadio
                            // screenshot, then inspect the supported compact layouts.
                            shell.Width = 1744;
                            shell.Height = 1090;
                            shell.UpdateLayout();
                            pages.Add(await Capture(shell, folder, "albums-reference-size"));
                            var browser = shell.ShellFrame.Content as NavidromeBrowserView;
                            var library = browser == null ? null : browser.FindName("LibraryItems") as GridView;
                            if (library != null && library.Items.Count > 0)
                            {
                                library.SelectedItems.Add(library.Items[0]);
                                var first = library.ContainerFromIndex(0) as Control;
                                if (first != null) first.Focus(FocusState.Keyboard);
                                pages.Add(await Capture(shell, folder, "albums-selected"));
                                library.SelectedItems.Clear();
                                if (first != null) first.Focus(FocusState.Keyboard);
                                pages.Add(await Capture(shell, folder, "albums-focus"));
                                if (first != null)
                                {
                                    VisualStateManager.GoToState(first, "PointerOver", false);
                                    pages.Add(await Capture(shell, folder, "albums-hover-preview"));
                                    VisualStateManager.GoToState(first, "Normal", false);
                                    var artwork = Descendants(first).OfType<Subsonic8.Common.MixRadioArtwork>().FirstOrDefault();
                                    if (artwork != null)
                                    {
                                        var binding = artwork.GetBindingExpression(Subsonic8.Common.MixRadioArtwork.SourceProperty);
                                        var source = artwork.Source;
                                        try
                                        {
                                            artwork.Source = null;
                                            pages.Add(await Capture(shell, folder, "albums-missing-cover-preview"));
                                        }
                                        finally
                                        {
                                            if (binding != null) artwork.SetBinding(Subsonic8.Common.MixRadioArtwork.SourceProperty, binding.ParentBinding);
                                            else artwork.Source = source;
                                        }
                                    }
                                }
                            }
                            foreach (var compactWidth in new[] { 900, 500 })
                            {
                                shell.Width = compactWidth;
                                shell.Height = 900;
                                shell.UpdateLayout();
                                pages.Add(await Capture(shell, folder, "albums-" + compactWidth));
                            }
                            shell.Width = 1744;
                            shell.Height = 1090;
                            shell.UpdateLayout();
                            if (NavigationExtensions.NavigateToViewModel<NavidromeBrowserViewModel>(navigation, "artists"))
                            {
                                pages.Add(await Capture(shell, folder, "artists"));
                                var artists = (NavidromeBrowserView)shell.ShellFrame.Content;
                                var filter = (Button)artists.FindName("CurrentFilter");
                                var peer = new ButtonAutomationPeer(filter);
                                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                                await Task.Delay(500);
                                var popup = VisualTreeHelper.GetOpenPopups(Window.Current).FirstOrDefault();
                                if (popup != null)
                                {
                                    var choices = Descendants(popup.Child).OfType<ListBox>().FirstOrDefault();
                                    report["libraryMenuScreenshot"] = JsonValue.CreateStringValue(await WriteScreenshot(popup.Child, folder, "library-menu"));
                                    report["libraryMenuChoiceCount"] = JsonValue.CreateNumberValue(choices == null ? 0 : choices.Items.Count);
                                    if (choices != null)
                                    {
                                        choices.SelectedItem = choices.Items.OfType<LibraryDestination>().First(destination => destination.Route == "albums");
                                        pages.Add(await Capture(shell, folder, "albums-from-menu"));
                                        var destinationPage = shell.ShellFrame.Content as NavidromeBrowserView;
                                        var destinationModel = destinationPage == null ? null : destinationPage.DataContext as NavidromeBrowserViewModel;
                                        report["libraryMenuNavigated"] = JsonValue.CreateBooleanValue(destinationModel != null && destinationModel.Parameter == "albums");
                                    }
                                }
                            }
                            if (NavigationExtensions.NavigateToViewModel<NavidromeBrowserViewModel>(navigation, "songs"))
                                pages.Add(await Capture(shell, folder, "songs"));
                            var indexChecks = new JsonArray();
                            report["indexChecks"] = indexChecks;
                            foreach (var section in new[] { "albums", "songs", "artists", "genres" })
                            {
                                if (!NavigationExtensions.NavigateToViewModel<NavidromeBrowserViewModel>(navigation, section)) continue;
                                pages.Add(await Capture(shell, folder, section + "-grouped"));
                                var indexedView = (NavidromeBrowserView)shell.ShellFrame.Content;
                                var indexedModel = (NavidromeBrowserViewModel)indexedView.DataContext;
                                var before = indexedModel.Items.Count;
                                var listBeforeIndex = (GridView)indexedView.FindName("LibraryItems");
                                var pagingSelection = indexedModel.Items.LastOrDefault();
                                if (pagingSelection != null && indexedModel.CanLoadMore)
                                {
                                    listBeforeIndex.SelectedItems.Add(pagingSelection);
                                    listBeforeIndex.ScrollIntoView(pagingSelection, ScrollIntoViewAlignment.Leading);
                                    await indexedModel.LoadMore();
                                    pages.Add(await Capture(shell, folder, section + "-paged"));
                                }
                                var pagingPreserved = pagingSelection == null || indexedModel.SelectedItems.Contains(pagingSelection);
                                listBeforeIndex.SelectedItems.Clear();
                                if (listBeforeIndex.Items.Count > 0) listBeforeIndex.ScrollIntoView(listBeforeIndex.Items[0], ScrollIntoViewAlignment.Leading);
                                listBeforeIndex.UpdateLayout();
                                await indexedView.ShowIndex();
                                pages.Add(await Capture(shell, folder, section + "-index"));
                                var target = indexedModel.IndexGroups.LastOrDefault(group => group.IsEnabled);
                                var check = new JsonObject();
                                check["section"] = JsonValue.CreateStringValue(section);
                                check["pagingSelectionPreserved"] = JsonValue.CreateBooleanValue(pagingPreserved);
                                check["beforeCount"] = JsonValue.CreateNumberValue(before);
                                check["afterCount"] = JsonValue.CreateNumberValue(indexedModel.Items.Count);
                                check["complete"] = JsonValue.CreateBooleanValue(!indexedModel.CanLoadMore && !indexedModel.HasError);
                                check["indexVisible"] = JsonValue.CreateBooleanValue(((Grid)indexedView.FindName("IndexSurface")).Visibility == Visibility.Visible);
                                check["groupCount"] = JsonValue.CreateNumberValue(indexedModel.IndexGroups.Count);
                                if (target != null)
                                {
                                    indexedView.JumpToIndex(target);
                                    pages.Add(await Capture(shell, folder, section + "-index-jump"));
                                    var indexedList = (GridView)indexedView.FindName("LibraryItems");
                                    var targetContainer = indexedList.ContainerFromItem(target) as FrameworkElement;
                                    check["target"] = JsonValue.CreateStringValue(target.Title);
                                    if (targetContainer != null)
                                    {
                                        var point = targetContainer.TransformToVisual(indexedList).TransformPoint(new Point());
                                        check["targetVisible"] = JsonValue.CreateBooleanValue(point.X >= 0 && point.Y >= 0 && point.X < indexedList.ActualWidth && point.Y < indexedList.ActualHeight);
                                    }
                                    indexedList.SelectedItems.Add(target);
                                    check["headerExcludedFromSelection"] = JsonValue.CreateBooleanValue(!indexedModel.SelectedItems.Contains(target) && !indexedList.SelectedItems.Contains(target));
                                    var firstMedia = indexedList.Items.OfType<NavidromeItem>().FirstOrDefault();
                                    if (firstMedia != null) indexedList.SelectedItems.Add(firstMedia);
                                    await indexedView.ShowIndex();
                                    check["mediaSelectionPreserved"] = JsonValue.CreateBooleanValue(firstMedia == null || indexedModel.SelectedItems.Contains(firstMedia));
                                    shell.Width = 500; shell.Height = 900; shell.UpdateLayout();
                                    pages.Add(await Capture(shell, folder, section + "-index-500"));
                                    shell.Width = 1744; shell.Height = 1090; shell.UpdateLayout();
                                }
                                indexChecks.Add(check);
                                await SaveReport(root, folder, report);
                            }
                        }
                        finally { shell.Width = originalWidth; shell.Height = originalHeight; shell.UpdateLayout(); }
                    }
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
            var header = new JsonObject();
            foreach (var elementName in new[] { "GoBack", "PageTitle", "CurrentFilter", "SearchControl" })
            {
                var element = page == null ? null : page.FindName(elementName) as FrameworkElement;
                if (element != null && element.Visibility == Visibility.Visible) header[elementName] = Bounds(element, page);
            }
            result["header"] = header;
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
                    var control = item as Control;
                    if (control != null) box["focusState"] = JsonValue.CreateStringValue(control.FocusState.ToString());
                    var focusVisual = Descendants(item).OfType<FrameworkElement>().FirstOrDefault(element => element.Name == "FocusVisualWhite");
                    if (focusVisual != null) box["focusVisualOpacity"] = JsonValue.CreateNumberValue(focusVisual.Opacity);
                    var media = list.Items[index] as NavidromeItem;
                    var card = Descendants(item).OfType<Grid>().FirstOrDefault(grid => Math.Abs(grid.ActualWidth - 260) < 0.1 && Math.Abs(grid.ActualHeight - 130) < 0.1);
                    if (card != null && media != null)
                    {
                        box["card"] = Bounds(card, page);
                        var texts = new JsonArray();
                        foreach (var block in Descendants(card).OfType<TextBlock>().Where(block => !string.IsNullOrEmpty(block.Text)))
                        {
                            var textBounds = Bounds(block, card);
                            var position = block.TransformToVisual(card).TransformPoint(new Point());
                            textBounds["isCount"] = JsonValue.CreateBooleanValue(block.Text == media.CountText);
                            textBounds["withinCard"] = JsonValue.CreateBooleanValue(position.X >= 0 && position.Y >= 0 &&
                                position.X + block.ActualWidth <= card.ActualWidth + 0.1 && position.Y + block.ActualHeight <= card.ActualHeight + 0.1);
                            texts.Add(textBounds);
                        }
                        box["texts"] = texts;
                    }
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
                result["screenshot"] = JsonValue.CreateStringValue(await WriteScreenshot(shell, folder, name));
            }
            catch (Exception exception) { result["captureError"] = JsonValue.CreateStringValue(exception.ToString()); }
            return result;
        }

        private static JsonObject Bounds(FrameworkElement element, UIElement relativeTo)
        {
            var point = element.TransformToVisual(relativeTo).TransformPoint(new Point());
            return new JsonObject {
                ["x"] = JsonValue.CreateNumberValue(point.X), ["y"] = JsonValue.CreateNumberValue(point.Y),
                ["width"] = JsonValue.CreateNumberValue(element.ActualWidth), ["height"] = JsonValue.CreateNumberValue(element.ActualHeight)
            };
        }

        private static async Task<string> WriteScreenshot(UIElement element, StorageFolder folder, string name)
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(element);
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
            return filename;
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
