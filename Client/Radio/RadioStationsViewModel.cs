namespace Mixdrome.Radio
{
    using System.Collections.Generic;
    using Client.Common.Models;
    using Client.Common.Models.Subsonic;
    using Client.Common.Results;
    using Mixdrome.Framework.ViewModel;

    public class RadioStationsViewModel : CollectionViewModelBase<object, IList<InternetRadioStation>>, IRadioStationsViewModel
    {
        public RadioStationsViewModel()
        {
            UpdateDisplayName = () => DisplayName = "radio";
        }

        public override string EmptyStateSubtitle
        {
            get
            {
                return "Add internet radio stations on the server and they will appear as bold launch tiles here.";
            }
        }

        public override string EmptyStateTitle
        {
            get
            {
                return "No radio stations";
            }
        }

        protected override async void OnActivate()
        {
            base.OnActivate();
            await Populate();
        }

        protected override IEnumerable<IMediaModel> GetItemsToDisplay(IList<InternetRadioStation> result)
        {
            return result;
        }

        protected override IServiceResultBase<IList<InternetRadioStation>> GetResult(object parameter)
        {
            return SubsonicService.GetInternetRadioStations();
        }
    }
}
