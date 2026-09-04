namespace Client.Common.Results
{
    using System.Collections.Generic;
    using Client.Common.Models.Subsonic;

    public interface IGetNowPlayingResult : IServiceResultBase<IList<NowPlayingEntry>>
    {
    }
}
