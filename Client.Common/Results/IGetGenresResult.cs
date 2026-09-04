namespace Client.Common.Results
{
    using System.Collections.Generic;
    using Client.Common.Models.Subsonic;

    public interface IGetGenresResult : IServiceResultBase<IList<Genre>>
    {
    }
}
