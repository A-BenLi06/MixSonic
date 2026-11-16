namespace Mixdrome.Navidrome
{
    using Windows.ApplicationModel.Resources;

    public static class NavidromeText
    {
        public static string Get(string key) { return ResourceLoader.GetForCurrentView().GetString("Navidrome_Text_" + key); }
        public static string Label(string uid) { return ResourceLoader.GetForCurrentView().GetString(uid + "/Label"); }
    }
}
