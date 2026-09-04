namespace Client.Common.Results
{
    using System.Collections.Generic;
    using Client.Common.Models.Subsonic;

    public interface IGetSongsByGenreResult : IServiceResultBase<IList<Song>>
    {
        string Genre { get; }
    }
}
