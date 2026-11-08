namespace Subsonic8
{
    using Windows.ApplicationModel.Resources;

    public static class MixRadioText
    {
        public static string Get(string key)
        {
            return ResourceLoader.GetForCurrentView().GetString(key);
        }
    }
}
