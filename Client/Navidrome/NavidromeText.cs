namespace Subsonic8.Navidrome
{
    using Windows.ApplicationModel.Resources;

    public static class NavidromeText
    {
        public static string Get(string key) { return ResourceLoader.GetForCurrentView().GetString("Navidrome_" + key); }
    }
}
