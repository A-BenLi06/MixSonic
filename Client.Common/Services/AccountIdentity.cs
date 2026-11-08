namespace Client.Common.Services
{
    using System;
    using Client.Common.Services.DataStructures.SubsonicService;

    public static class AccountIdentity
    {
        public static string For(ISubsonicServiceConfiguration configuration)
        {
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.BaseUrl)) return string.Empty;
            Uri uri;
            if (!Uri.TryCreate(configuration.BaseUrl, UriKind.Absolute, out uri)) return string.Empty;
            return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/') +
                   "\n" + configuration.Username;
        }
    }
}
