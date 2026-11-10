namespace Subsonic8.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;

    public sealed class LibraryDestination
    {
        public string Route { get; private set; }
        public string Label { get; private set; }
        public string Glyph { get; private set; }
        public LibraryDestination(string route, string label, string glyph)
        { Route = route; Label = label; Glyph = glyph; }

        public void Navigate(INavigationService navigation)
        {
            if (Route == "downloads") navigation.NavigateToViewModel<NavidromeDownloadsViewModel>();
            else if (Route == "account") navigation.NavigateToViewModel<NavidromeAccountViewModel>();
            else navigation.NavigateToViewModel<NavidromeBrowserViewModel>(Route);
        }

        public static IList<LibraryDestination> All()
        {
            return new[] {
                Section("albums", "\uE1AB"), Section("artists", "\uE13D"), Section("songs", "\uE189"),
                Section("genres", "\uE14C"), Section("playlists", "\uE142"), Section("random", "\uE14B"),
                Section("favourites", "\uE113"), Section("radio", "\uE15D"), Section("activity", "\uE121"),
                Section("bookmarks", "\uE12F"), Section("shares", "\uE14D"),
                new LibraryDestination("downloads", NavidromeText.Get("Downloads"), "\uE118"),
                new LibraryDestination("account", NavidromeText.Get("Account"), "\uE13D")
            };
        }
        private static LibraryDestination Section(string route, string glyph)
        { return new LibraryDestination(route, NavidromeText.Get("Section" + route), glyph); }
    }

    public sealed class HomeSection : PropertyChangedBase
    {
        private bool _loading;
        private string _error;
        public HomeSection(string route, string title, string emptyText, string glyph)
        {
            Destination = new LibraryDestination(route, NavidromeText.Get("SeeAll"), glyph);
            Title = title; EmptyText = emptyText; Items = new BindableCollection<object>();
            Replace(Enumerable.Empty<NavidromeItem>());
        }
        public string Title { get; private set; }
        public string EmptyText { get; private set; }
        public LibraryDestination Destination { get; private set; }
        public BindableCollection<object> Items { get; private set; }
        public bool HasMusic { get { return Items.OfType<NavidromeItem>().Any(); } }
        public bool IsEmpty { get { return !IsLoading && !HasMusic && string.IsNullOrEmpty(Error); } }
        public bool IsLoading { get { return _loading; } set { _loading = value; NotifyOfPropertyChange(); NotifyOfPropertyChange(() => IsEmpty); } }
        public string Error { get { return _error; } set { _error = value; NotifyOfPropertyChange(); NotifyOfPropertyChange(() => IsEmpty); } }
        public void Replace(IEnumerable<NavidromeItem> items, bool hero = false)
        {
            Items.Clear();
            var music = items.ToList();
            if (hero && music.Count > 0) music[0].IsHero = true;
            Items.AddRange(music.Cast<object>()); Items.Add(Destination);
            NotifyOfPropertyChange(() => HasMusic); NotifyOfPropertyChange(() => IsEmpty);
        }
    }

    public sealed class MixRadioHomeTemplateSelector : DataTemplateSelector
    {
        public DataTemplate AlbumTemplate { get; set; }
        public DataTemplate ArtistTemplate { get; set; }
        public DataTemplate PlaylistTemplate { get; set; }
        public DataTemplate HeroTemplate { get; set; }
        public DataTemplate LinkTemplate { get; set; }
        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is LibraryDestination) return LinkTemplate;
            var media = item as NavidromeItem;
            if (media == null) return AlbumTemplate;
            if (media.IsHero && HeroTemplate != null) return HeroTemplate;
            if (media.Media != null && media.Media.Type == SubsonicModelTypeEnum.Artist) return ArtistTemplate;
            if (media.Media != null && media.Media.Type == SubsonicModelTypeEnum.Playlist) return PlaylistTemplate;
            return AlbumTemplate;
        }
    }

    public sealed class MixRadioLibraryTemplateSelector : DataTemplateSelector
    {
        public DataTemplate AlbumTemplate { get; set; }
        public DataTemplate ArtistTemplate { get; set; }
        public DataTemplate SongTemplate { get; set; }
        public DataTemplate TextTemplate { get; set; }
        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            var media = item as NavidromeItem;
            if (media == null || media.Media == null) return TextTemplate;
            if (media.Song != null) return SongTemplate;
            if (media.Media.Type == SubsonicModelTypeEnum.Artist) return ArtistTemplate;
            if (media.Media.Type == SubsonicModelTypeEnum.Genre || media.Media.Type == SubsonicModelTypeEnum.InternetRadioStation) return TextTemplate;
            return AlbumTemplate;
        }
    }

    public sealed class MixRadioLibraryContainerStyleSelector : StyleSelector
    {
        public Style AlbumStyle { get; set; }
        public Style SongStyle { get; set; }
        protected override Style SelectStyleCore(object item, DependencyObject container)
        {
            var media = item as NavidromeItem;
            return media == null || media.Media == null || media.Song != null ||
                media.Media.Type == SubsonicModelTypeEnum.Genre || media.Media.Type == SubsonicModelTypeEnum.InternetRadioStation
                ? SongStyle : AlbumStyle;
        }
    }

    public sealed class MixRadioMosaicGridView : GridView
    {
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);
            var tile = item as NavidromeItem;
            var span = tile != null && tile.IsHero ? 2 : 1;
            var container = (UIElement)element;
            VariableSizedWrapGrid.SetColumnSpan(container, span);
            VariableSizedWrapGrid.SetRowSpan(container, span);
        }
    }
}
