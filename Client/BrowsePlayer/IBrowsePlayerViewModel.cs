namespace Subsonic8.BrowsePlayer
{
    using System.Threading.Tasks;
    using Caliburn.Micro;

    public interface IBrowsePlayerViewModel : IScreen
    {
        Task LoadRecentAsync();
    }
}
