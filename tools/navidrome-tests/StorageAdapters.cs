// CLR adapters for production service tests. They do not certify WinRT or native audio.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Windows.Storage
{
    public enum CreationCollisionOption { ReplaceExisting, OpenIfExists }
    public enum NameCollisionOption { ReplaceExisting, FailIfExists }
    public sealed class ApplicationData
    {
        public static readonly ApplicationData Current = new ApplicationData();
        public StorageFolder LocalFolder { get; private set; }
        public Settings LocalSettings = new Settings();
        public void UseRoot(string path) { Directory.CreateDirectory(path); LocalFolder = new StorageFolder(path); LocalSettings.Values.Clear(); }
    }
    public sealed class Settings { public Dictionary<string, object> Values = new Dictionary<string, object>(); }
    public sealed class BasicProperties { public ulong Size; }
    public sealed class StorageFolder
    {
        public string Path { get; private set; }
        public StorageFolder(string path) { Path = System.IO.Path.GetFullPath(path); }
        internal string Child(string name)
        {
            var resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, name));
            if (!resolved.StartsWith(Path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileName(name) != name)
                throw new InvalidDataException("Test adapter rejected a path outside its folder.");
            return resolved;
        }
        public Task<StorageFolder> CreateFolderAsync(string name, CreationCollisionOption collision)
        { var path = Child(name); Directory.CreateDirectory(path); return Task.FromResult(new StorageFolder(path)); }
        public Task<StorageFile> CreateFileAsync(string name, CreationCollisionOption collision)
        { var path = Child(name); if (collision == CreationCollisionOption.ReplaceExisting || !File.Exists(path)) File.WriteAllBytes(path, new byte[0]); return Task.FromResult(new StorageFile(path)); }
        public Task<StorageFile> GetFileAsync(string name)
        { var path = Child(name); if (!File.Exists(path)) throw new FileNotFoundException(); return Task.FromResult(new StorageFile(path)); }
    }
    public sealed class StorageFile
    {
        public string Path { get; private set; }
        public StorageFile(string path) { Path = path; }
        public Task<BasicProperties> GetBasicPropertiesAsync() { return Task.FromResult(new BasicProperties { Size = (ulong)new FileInfo(Path).Length }); }
        public Task<Stream> OpenStreamForWriteAsync() { return Task.FromResult<Stream>(new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.None)); }
        public Task RenameAsync(string name, NameCollisionOption collision)
        {
            var target = new StorageFolder(System.IO.Path.GetDirectoryName(Path)).Child(name);
            if (File.Exists(target)) { if (collision == NameCollisionOption.FailIfExists) throw new IOException("Target exists."); File.Delete(target); }
            File.Move(Path, target); Path = target; return Task.FromResult(true);
        }
        public Task DeleteAsync() { File.Delete(Path); return Task.FromResult(true); }
    }
    public static class FileIO
    {
        public static Task<string> ReadTextAsync(StorageFile file) { return Task.FromResult(File.ReadAllText(file.Path, System.Text.Encoding.UTF8)); }
        public static Task WriteTextAsync(StorageFile file, string text) { File.WriteAllText(file.Path, text, new System.Text.UTF8Encoding(false)); return Task.FromResult(true); }
    }
}
namespace Windows.Security.Cryptography
{
    public enum BinaryStringEncoding { Utf8 }
    public static class CryptographicBuffer
    {
        public static byte[] ConvertStringToBinary(string text, BinaryStringEncoding encoding) { return System.Text.Encoding.UTF8.GetBytes(text); }
        public static string EncodeToHexString(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
}
namespace Windows.Security.Cryptography.Core
{
    public static class HashAlgorithmNames { public const string Sha256 = "SHA256"; }
    public sealed class HashAlgorithmProvider
    {
        public static HashAlgorithmProvider OpenAlgorithm(string name) { return new HashAlgorithmProvider(); }
        public byte[] HashData(byte[] bytes) { using (var hash = System.Security.Cryptography.SHA256.Create()) return hash.ComputeHash(bytes); }
    }
}
namespace Windows.ApplicationModel.Resources
{
    public sealed class ResourceLoader
    {
        public static ResourceLoader GetForCurrentView() { return new ResourceLoader(); }
        public string GetString(string key) { return key; }
    }
}
namespace Windows.UI.Xaml
{
    public sealed class DispatcherTimer
    {
        private static readonly List<DispatcherTimer> Timers = new List<DispatcherTimer>();
        public DispatcherTimer() { Timers.Add(this); }
        public TimeSpan Interval { get; set; }
        public event EventHandler<object> Tick;
        public bool Enabled;
        public void Start() { Enabled = true; }
        public void Stop() { Enabled = false; }
        public static void Pulse() { foreach (var timer in Timers.ToList()) if (timer.Enabled && timer.Tick != null) timer.Tick(timer, null); }
    }
    public sealed class Application
    {
        public static readonly Application Current = new Application();
        public event EventHandler<object> Resuming;
        public void Resume() { if (Resuming != null) Resuming(this, null); }
    }
}
#if !NATIVE_PLAYER_TESTS
namespace Subsonic8.AudioPlayback
{
    public interface IAudioPlayerViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        event EventHandler<EventArgs> PlaybackStarted;
        event EventHandler<EventArgs> PlaybackPaused;
        event EventHandler<EventArgs> PlaybackStoped;
        event EventHandler PlaybackSeeked;
        Client.Common.Models.PlaylistItem CurrentItem { get; }
        bool IsActuallyPlaying { get; }
        double PlaybackRate { get; }
        TimeSpan LastStoppedPosition { get; }
        TimeSpan GetCurrentPosition();
        TimeSpan GetDuration();
        void Pause();
    }
}
#endif
