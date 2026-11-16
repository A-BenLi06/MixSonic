namespace Mixdrome.Search
{
    using System.Collections.Generic;
    using System.Linq;
    using Mixdrome.Framework.ViewModel;
    using Mixdrome.MenuItem;

    public interface ISearchViewModel : ICollectionViewModel<string>
    {
        #region Public Properties

        IEnumerable<IGrouping<string, MenuItemViewModel>> GroupedMenuItems { get; }

        SearchResultState State { get; }

        #endregion
    }
}
