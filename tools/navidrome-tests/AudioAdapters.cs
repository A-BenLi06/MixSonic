// Native event/control adapters for production audio logic. No audio is decoded.
namespace MugenInjection.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Property)]
    public sealed class InjectAttribute : System.Attribute { }
}
namespace Windows.UI.Xaml
{
    public class RoutedEventArgs : System.EventArgs { public object OriginalSource { get; set; } }
    public sealed class ExceptionRoutedEventArgs : RoutedEventArgs { public string ErrorMessage { get; set; } }
}
namespace Windows.UI.Xaml.Media
{
    public enum MediaElementState { Closed, Opening, Buffering, Playing, Paused, Stopped }
}
namespace Windows.UI.Xaml.Controls
{
    public sealed class MediaElement { public Windows.UI.Xaml.Media.MediaElementState CurrentState { get; set; } }
}
