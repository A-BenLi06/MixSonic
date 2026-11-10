// CLR boundaries for the home controller. Rendering and native navigation are not simulated evidence.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Caliburn.Micro;
using Client.Common.Models;
using Client.Common.Models.Subsonic;
using Client.Common.Services.Navidrome;

namespace MugenInjection.Attributes
{
    [AttributeUsage(AttributeTargets.Property)] public sealed class InjectAttribute : Attribute { }
}
namespace Windows.UI.Xaml
{
    public class DependencyObject { }
    public class UIElement : DependencyObject { }
    public class FrameworkElement : UIElement { }
    public class DataTemplate { }
    public class Style { }
}
namespace Windows.UI.Xaml.Controls
{
    public class StyleSelector
    {
        protected virtual Windows.UI.Xaml.Style SelectStyleCore(object item, Windows.UI.Xaml.DependencyObject container) { return null; }
    }
    public sealed class ItemClickEventArgs : EventArgs { public object ClickedItem { get; set; } }
    public class DataTemplateSelector
    {
        protected virtual Windows.UI.Xaml.DataTemplate SelectTemplateCore(object item, Windows.UI.Xaml.DependencyObject container) { return null; }
        public Windows.UI.Xaml.DataTemplate Select(object item) { return SelectTemplateCore(item, null); }
    }
    public class GridView : Windows.UI.Xaml.FrameworkElement
    { protected virtual void PrepareContainerForItemOverride(Windows.UI.Xaml.DependencyObject element, object item) { } }
    public static class VariableSizedWrapGrid
    {
        public static void SetColumnSpan(Windows.UI.Xaml.UIElement element, int span) { }
        public static void SetRowSpan(Windows.UI.Xaml.UIElement element, int span) { }
    }
}
namespace Caliburn.Micro
{
    public interface INavigationService { void Navigate(Type page, object parameter); }
    public static class NavigationExtensions
    {
        public static void NavigateToViewModel<T>(this INavigationService navigation, object parameter = null)
        { navigation.Navigate(typeof(T), parameter); }
    }
}
namespace Subsonic8.Framework.ViewModel
{
    public interface IViewModel : IScreen { }
    public abstract class ViewModelBase : Screen, IViewModel
    {
        public Action UpdateDisplayName { get; set; }
        public IEventAggregator EventAggregator { get; set; }
        public INavigationService NavigationService { get; set; }
        public ErrorDialogAdapter ErrorDialogViewModel = new ErrorDialogAdapter();
        public sealed class ErrorDialogAdapter { public void Hide() { } }
    }
}
namespace Subsonic8.BottomBar { public sealed class ChangeBottomBarMessage { public object BottomBarViewModel { get; set; } } }
namespace Subsonic8.Framework.Services { public interface IDialogService { void ShowSettings<T>() where T : Screen; } }
namespace Subsonic8.Settings { public sealed class SettingsViewModel : Screen { } }
namespace Subsonic8.Navidrome
{
    public interface INavidromePlayerViewModel : IScreen { }
    public sealed class NavidromePlayerViewModel : Screen, INavidromePlayerViewModel, Subsonic8.Framework.ViewModel.IHaveState
    {
        public Dictionary<string, object> Saved, Loaded;
        public void SaveState(Dictionary<string, object> state, List<Type> knownTypes) { Saved = state; }
        public void LoadState(string parameter, Dictionary<string, object> state) { Loaded = state; }
    }
    public sealed class NavidromeBrowserViewModel { }
    public sealed class NavidromeDownloadsViewModel { }
    public sealed class NavidromeAccountViewModel { }
    public sealed class NavidromeDetailViewModel { }
    public interface IMediaQueueService
    {
        Task<IList<Song>> ResolveAsync(IEnumerable<ISubsonicModel> items);
        void Play(NavidromeApi api, IEnumerable<Song> songs, int index = 0, long positionMilliseconds = 0);
        void PlayLocal(IEnumerable<PlaylistItem> songs, int index = 0);
    }
    public interface IOfflineLibraryService
    {
        BindableCollection<OfflineDownloadItem> Entries { get; }
        Task RefreshAsync();
        Task<IList<PlaylistItem>> CreateQueueAsync(IEnumerable<OfflineDownloadItem> items);
    }
}
