namespace Mixdrome.Phone.Common
{
    using System.Globalization;
    using Windows.ApplicationModel.Resources;
    public static class PhoneText
    {
        public static string Get(string key) { return ResourceLoader.GetForCurrentView().GetString("Phone_Text_" + key); }
        public static string Format(string key, params object[] values) { return string.Format(CultureInfo.CurrentCulture, Get(key), values); }
    }
}
