namespace Mixdrome.Framework.Services
{
    public interface IIoCService
    {
        #region Public Methods and Operators

        T Get<T>();

        #endregion
    }
}