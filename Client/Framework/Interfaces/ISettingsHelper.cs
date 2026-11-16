namespace Mixdrome.Framework.Interfaces
{
    using System.Threading.Tasks;
    using Windows.Security.Credentials;

    public interface ISettingsHelper
    {
        #region Public Methods and Operators

        PasswordCredential GetCredentialsFromVault();

        Task LoadSettings();

        void UpdateCredentialsInVault(PasswordCredential credential);

        #endregion
    }
}