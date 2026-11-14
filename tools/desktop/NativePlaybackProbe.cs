namespace Subsonic8.Diagnostics
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices.WindowsRuntime;
    using System.Threading.Tasks;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Client.Common.Services;
    using Microsoft.Practices.ServiceLocation;
    using Subsonic8.AudioPlayback;
    using Subsonic8.Framework.Interfaces;
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

    // Explicit diagnostic build only. Uses isolated local PCM and a private
    // queue: no account changes, server mutations or personal media playback.
    internal static class NativePlaybackProbe
    {
        private sealed class Controls : IExtendedPlayerControls
        {
            internal readonly MediaElement Media = new MediaElement { AutoPlay = false };
            internal int Seeks;
            public TimeSpan GetCurrentPosition() { return Media.Position; }
            public TimeSpan GetCurrentDuration() { return Media.NaturalDuration.HasTimeSpan ? Media.NaturalDuration.TimeSpan : TimeSpan.Zero; }
            public bool CanSeek { get { return Media.CanSeek; } }
            public void Seek(TimeSpan position) { Seeks++; Media.Position = position; }
            public void Play() { Media.Play(); }
            public void Pause() { Media.Pause(); }
            public void Stop() { Media.Stop(); }
            public double Volume { get { return Media.Volume; } set { Media.Volume = value; } }
            public bool IsMuted { get { return Media.IsMuted; } set { Media.IsMuted = value; } }
            public double PlaybackRate { get { return Media.PlaybackRate; } set { Media.PlaybackRate = value; } }
        }

        internal static async Task RunAsync(ShellView shell)
        {
            var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("native-playback-probe", CreationCollisionOption.OpenIfExists);
            var checks = new JsonArray();
            var report = new JsonObject { ["createdAt"] = JsonValue.CreateStringValue(DateTimeOffset.Now.ToString("O")), ["checks"] = checks };
            Action<bool, string> check = (passed, name) =>
            {
                checks.Add(new JsonObject { ["name"] = JsonValue.CreateStringValue(name), ["passed"] = JsonValue.CreateBooleanValue(passed) });
                if (!passed) throw new InvalidOperationException(name);
            };
            var host = (Grid)shell.Content;
            var overlay = new Grid { Background = new SolidColorBrush(Windows.UI.Colors.Black) };
            var controls = new Controls();
            var audio = new AudioPlayerViewModel { EventAggregator = new EventAggregator(), PlayerControls = controls };
            try
            {
                var session = ServiceLocator.Current.GetInstance<INavidromeSessionService>();
                var queue = new PlaylistManagementService(new EventAggregator());
                var item = new PlaylistItem { Title = "Native playback probe", Artist = "Local PCM", Duration = 60,
                    SongId = "native-playback-probe", AccountKey = session.AccountKey };
                queue.Items.Add(item);
                var model = new NavidromePlayerViewModel { Audio = audio, PlaylistQueue = queue, Session = session };
                var surface = new MixRadioPlayerSurface { DataContext = model, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                controls.Media.IsMuted = true;
                overlay.Children.Add(controls.Media); overlay.Children.Add(surface); host.Children.Add(overlay);
                var file = await folder.CreateFileAsync("sample.wav", CreationCollisionOption.ReplaceExisting);
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(System.Text.Encoding.UTF8.GetBytes("RIFF")); writer.Write(36 + 60 * 8000 * 2);
                    writer.Write(System.Text.Encoding.UTF8.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                    writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
                    writer.Write(System.Text.Encoding.UTF8.GetBytes("data")); writer.Write(60 * 8000 * 2);
                    for (var sample = 0; sample < 60 * 8000; sample++) writer.Write((short)(1000 * Math.Sin(sample * 2 * Math.PI * 440 / 8000)));
                    await FileIO.WriteBytesAsync(file, stream.ToArray());
                }
                item.Uri = new Uri("ms-appdata:///local/native-playback-probe/sample.wav");
                audio.PropertyChanged += (sender, args) => { if (args.PropertyName == "Source") controls.Media.Source = audio.Source; };
                controls.Media.MediaOpened += (sender, args) => audio.MediaOpened();
                controls.Media.CurrentStateChanged += (sender, args) => audio.MediaStateChanged(sender, args);
                controls.Media.MediaFailed += (sender, args) => audio.SongFailed(args);
                audio.PlaybackSeeked += (sender, args) => model.UpdateTiming();
                audio.Play(item); audio.Pause();
                await Until(() => audio.CanSeek || !string.IsNullOrEmpty(audio.PlaybackError));
                check(audio.CanSeek && string.IsNullOrEmpty(audio.PlaybackError), "Local PCM opens in the native MediaElement");
                model.UpdateTiming();
                check(Math.Abs(model.DurationSeconds - 60) < 0.1, "Seek range uses decoded duration");
                var start = audio.GetCurrentPosition(); var count = controls.Seeks;
                model.BeginScrub();
                for (var index = 1; index <= 200; index++) { model.PositionSeconds = index / 5.0; model.UpdateTiming(); }
                check(model.IsScrubbing && model.PositionSeconds == 40 && controls.Seeks == count && audio.GetCurrentPosition() == start,
                    "Held scrub previews 200 updates without native seeks or timer snapback");
                model.FinishScrub(true); model.FinishScrub(true);
                await Until(() => Math.Abs(audio.GetCurrentPosition().TotalSeconds - 40) < 0.25);
                check(controls.Seeks == count + 1, "Release and capture loss commit exactly one native seek");
                count = controls.Seeks; model.BeginScrub(); model.PositionSeconds = 12; model.FinishScrub(false);
                check(controls.Seeks == count && Math.Abs(model.PositionSeconds - 40) < 0.25, "Canceled scrub restores actual playback position");
                model.BeginScrub(); model.PositionSeconds = 22; queue.Items.Remove(item); model.UpdateTiming(); model.FinishScrub(true);
                check(!model.IsScrubbing && controls.Seeks == count, "Removed occurrence cannot receive a stale scrub commit");
                queue.Items.Add(item); model.UpdateTiming(); model.BeginScrub(); audio.Stop(); model.UpdateTiming(); model.FinishScrub(true);
                check(!model.IsScrubbing && controls.Seeks == count, "Stopped media cancels scrub");
                audio.Play(item); audio.Pause(); await Until(() => audio.CanSeek); model.UpdateTiming();
                model.PositionSeconds = 15; count = controls.Seeks; model.PositionSeconds = 15;
                check(controls.Seeks == count, "Unchanged binding values cannot repeat a seek");
                model.PositionSeconds = double.NaN; model.PositionSeconds = double.PositiveInfinity;
                check(controls.Seeks == count && model.PositionSeconds == 15, "Nonfinite positions are rejected");
                model.BeginScrub(); model.PositionSeconds = 999; model.FinishScrub(true);
                check(controls.Seeks == count + 1, "Scrub commits within decoded duration");
                audio.Stop(); audio.Play(item); audio.Pause(); await Until(() => audio.CanSeek); model.UpdateTiming();
                model.Refresh();
                foreach (var width in new[] { 1744, 900, 500 })
                {
                    surface.Width = width; surface.Height = 900; surface.SetViewport(width, 1090); surface.UpdateLayout();
                    await Task.Delay(100); host.UpdateLayout();
                    await WriteScreenshot(surface, folder, "player-" + width + ".png");
                    var track = (StackPanel)surface.FindName("TrackInformation");
                    var transport = (FrameworkElement)surface.FindName("Transport");
                    var slider = (Slider)surface.FindName("SeekSlider");
                    var previous = (Button)surface.FindName("PreviousButton");
                    var rating = (FrameworkElement)track.Children.Last();
                    report["geometry-" + width] = new JsonObject {
                        ["trackHeight"] = JsonValue.CreateNumberValue(track.ActualHeight),
                        ["trackDesiredHeight"] = JsonValue.CreateNumberValue(track.DesiredSize.Height),
                        ["ratingBottom"] = JsonValue.CreateNumberValue(rating.TransformToVisual(track).TransformPoint(new Point(0, rating.ActualHeight)).Y),
                        ["rowSpan"] = JsonValue.CreateNumberValue(Grid.GetRowSpan(track)) };
                    check(rating.TransformToVisual(track).TransformPoint(new Point(0, rating.ActualHeight)).Y <= track.ActualHeight + 0.1,
                        "Favourite and rating fit inside the track details at " + width);
                    var sliderBottom = slider.TransformToVisual(transport).TransformPoint(new Point(0, slider.ActualHeight)).Y;
                    var buttonTop = previous.TransformToVisual(transport).TransformPoint(new Point()).Y;
                    check(sliderBottom <= buttonTop + 0.1, "Seek slider does not overlap transport buttons at " + width);
                    foreach (var state in new[] { "Pressed", "PointerOver", "Disabled", "Normal" })
                    {
                        check(VisualStateManager.GoToState(slider, state, false), "Native slider state " + state + " at " + width);
                        surface.UpdateLayout();
                    }
                }
                audio.Resume(); await Until(() => audio.IsActuallyPlaying); await Task.Delay(500);
                check(audio.IsActuallyPlaying && string.IsNullOrEmpty(audio.PlaybackError), "Native playback resumes after repeated scrub interactions");
                report["passed"] = JsonValue.CreateBooleanValue(true);
                report["limitations"] = JsonValue.CreateStringValue("Local PCM and native XAML/control states; real pointer injection and server codec seeking are not certified.");
            }
            catch (Exception exception) { report["passed"] = JsonValue.CreateBooleanValue(false); report["error"] = JsonValue.CreateStringValue(exception.ToString()); }
            finally
            {
                audio.Stop(); host.Children.Remove(overlay);
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("result.json", CreationCollisionOption.ReplaceExisting), report.Stringify());
            }
        }

        private static async Task Until(Func<bool> ready)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (!ready()) { if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("Native playback did not reach its expected state."); await Task.Delay(50); }
        }
        private static async Task WriteScreenshot(FrameworkElement element, StorageFolder folder, string name)
        {
            var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
            var pixels = await bitmap.GetPixelsAsync(); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
                await encoder.FlushAsync();
            }
        }
    }
}
