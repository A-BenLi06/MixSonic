namespace Mixdrome.BrowsePlayer
{
    using System.Threading.Tasks;
    using Caliburn.Micro;

    public interface IBrowsePlayerViewModel : IScreen
    {
        Task LoadRecentAsync();
    }
}
