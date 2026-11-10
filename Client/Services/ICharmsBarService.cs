
namespace Subsonic8.Services
{
    using Windows.Foundation;
    using Windows.UI.ApplicationSettings;

    public interface ICharmsBarService
    {
        void RegisterSettingsRequestedHandler(TypedEventHandler<SettingsPane, SettingsPaneCommandsRequestedEventArgs> handler);
    }
}
