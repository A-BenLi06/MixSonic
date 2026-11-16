namespace Mixdrome.Listening
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Threading.Tasks;

    public interface IListeningHistoryService : INotifyPropertyChanged
    {
        IEnumerable<RecentTrack> Entries { get; }

        string Error { get; }

        Task RefreshAsync();
    }
}
