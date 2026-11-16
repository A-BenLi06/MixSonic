
namespace Mixdrome.Services
{
    using Windows.Foundation;
    using Windows.UI.ApplicationSettings;

    public interface ICharmsBarService
    {
        void RegisterSettingsRequestedHandler(TypedEventHandler<SettingsPane, SettingsPaneCommandsRequestedEventArgs> handler);
    }
}
