namespace Mixdrome.ArtistInfo
{
    using System.Threading.Tasks;
    using Mixdrome.Framework.ViewModel;

    public interface IArtistInfoViewModel : IViewModel
    {
        Task Populate();

        string Parameter { get; set; }
    }
}