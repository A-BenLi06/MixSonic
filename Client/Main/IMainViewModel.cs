namespace Subsonic8.Main
{
    using System.Threading.Tasks;
    using Subsonic8.Framework.ViewModel;
    public interface IMainViewModel : IViewModel
    {
        Task Populate();
    }
}
