namespace Client.Common.Services.DataStructures.SubsonicService
{
    public static class SubsonicServiceConfigurationExtensions
    {
        #region Public Methods and Operators

        public static string RequestFormat(this ISubsonicServiceConfiguration configuration)
        {
            return configuration.BaseUrl + "rest/{0}?v=1.16.1&c=Subsonic8";
        }

        public static string RequestFormatWithUsernameAndPassword(this ISubsonicServiceConfiguration configuration)
        {
            return configuration.BaseUrl + "rest/{0}?u={1}&p={2}&v=1.16.1&c=Subsonic8";
        }

        #endregion
    }
}
