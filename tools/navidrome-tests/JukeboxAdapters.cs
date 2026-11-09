// Rendering and unused page-service adapters. Queue/API/page operation logic is production code.
namespace MugenInjection.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Property)]
    public sealed class InjectAttribute : System.Attribute { }
}
namespace Windows.UI.Xaml
{
    public class FrameworkElement { }
}
namespace Windows.UI.Xaml.Controls
{
    public sealed class ItemClickEventArgs : System.EventArgs { public object ClickedItem { get; set; } }
}
namespace Subsonic8.Framework.ViewModel
{
    public abstract class ViewModelBase : Caliburn.Micro.Screen
    {
        public System.Action UpdateDisplayName { get; set; }
        public Caliburn.Micro.IEventAggregator EventAggregator { get; set; }
        public ErrorDialogAdapter ErrorDialogViewModel = new ErrorDialogAdapter();
        public sealed class ErrorDialogAdapter { public void Hide() { } }
    }
}
namespace Subsonic8.BottomBar
{
    public sealed class ChangeBottomBarMessage { public object BottomBarViewModel { get; set; } }
}
namespace Subsonic8.Navidrome
{
    public interface IMediaQueueService { }
    public interface IOfflineLibraryService { }
}
