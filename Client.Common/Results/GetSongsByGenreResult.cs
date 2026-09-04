namespace Client.Common.Results
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using System.Xml.Serialization;
    using Client.Common.Models.Subsonic;
    using Client.Common.Services.DataStructures.SubsonicService;

    public class GetSongsByGenreResult : ServiceResultBase<IList<Song>>, IGetSongsByGenreResult
    {
        #region Constants

        private const int DefaultSongCount = 500;

        #endregion

        #region Constructors and Destructors

        public GetSongsByGenreResult(ISubsonicServiceConfiguration configuration, string genre)
            : base(configuration)
        {
            Genre = genre;
        }

        #endregion

        #region Public Properties

        public string Genre { get; private set; }

        public override string RequestUrl
        {
            get
            {
                return string.Format(
                    "{0}&genre={1}&count={2}",
                    base.RequestUrl,
                    Uri.EscapeDataString(Genre ?? string.Empty),
                    DefaultSongCount);
            }
        }

        public override string ResourcePath
        {
            get
            {
                return "getSongsByGenre.view";
            }
        }

        #endregion

        #region Methods

        public override void HandleResponse(XDocument xDocument)
        {
            var response = xDocument.Element(Namespace + "subsonic-response");
            var songsByGenreElement = response?.Element(Namespace + "songsByGenre");
            if (songsByGenreElement == null)
            {
                Result = new List<Song>();
                return;
            }

            var xmlSerializer = new XmlSerializer(typeof(Song));
            Result = songsByGenreElement.Elements(Namespace + "song")
                .Select(
                    song =>
                    {
                        using (var xmlReader = song.CreateReader())
                        {
                            return (Song)xmlSerializer.Deserialize(xmlReader);
                        }
                    })
                .ToList();
        }

        #endregion
    }
}
