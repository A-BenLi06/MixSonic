namespace Mixdrome.Main
{
    using System.Threading.Tasks;
    using Mixdrome.Framework.ViewModel;
    public interface IMainViewModel : IViewModel
    {
        Task Populate();
    }
}
