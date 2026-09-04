namespace Client.Common.Results
{
    using System.Collections.Generic;
    using Client.Common.Models.Subsonic;

    public interface IGetAllArtistsResult : IServiceResultBase<IList<ExpandedArtist>>
    {
    }
}
