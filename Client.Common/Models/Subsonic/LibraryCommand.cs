namespace Client.Common.Models.Subsonic
{
    using System;

    public class LibraryCommand : MediaModelBase
    {
        #region Constructors and Destructors

        public LibraryCommand()
        {
        }

        public LibraryCommand(string id, string name, string subtitle, SubsonicModelTypeEnum type)
        {
            Id = id;
            Name = name;
            Subtitle = subtitle;
            CommandType = type;
        }

        #endregion

        #region Public Properties

        public SubsonicModelTypeEnum CommandType { get; set; }

        public string Subtitle { get; set; }

        public override SubsonicModelTypeEnum Type
        {
            get
            {
                return CommandType;
            }
        }

        #endregion

        #region Public Methods and Operators

        public override Tuple<string, string> GetDescription()
        {
            return new Tuple<string, string>(Name, Subtitle);
        }

        #endregion
    }
}
