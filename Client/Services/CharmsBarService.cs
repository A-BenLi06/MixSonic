
namespace Subsonic8.Services
{
    using Windows.Foundation;
    using Windows.UI.ApplicationSettings;

    public class CharmsBarService : ICharmsBarService
    {
        public void RegisterSettingsRequestedHandler(TypedEventHandler<SettingsPane, SettingsPaneCommandsRequestedEventArgs> handler)
        {
            SettingsPane.GetForCurrentView().CommandsRequested += handler;
        }
    }
}
