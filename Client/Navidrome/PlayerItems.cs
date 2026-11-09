namespace Subsonic8.Navidrome
{
    using System;
    using System.Globalization;
    using Caliburn.Micro;
    using Client.Common.Models;
    using Client.Common.Models.Navidrome;

    public sealed class LyricChoice
    {
        public LyricVariant Variant { get; set; }
        public string Name
        {
            get { return NavidromeItem.Join(Variant.Language, Variant.Kind, NavidromeText.Get(Variant.Synced ? "SyncedLyrics" : "PlainLyrics")); }
        }
    }
    public sealed class LyricLineItem : PropertyChangedBase
    {
        private bool _isCurrent;
        public string Text { get; set; }
        public bool IsCurrent { get { return _isCurrent; } set { if (_isCurrent == value) return; _isCurrent = value; NotifyOfPropertyChange(); } }
    }
    public sealed class QueueEntryItem : PropertyChangedBase
    {
        private bool _isCurrent;
        public PlaylistItem Item { get; set; }
        public int Index { get; set; }
        public string Number { get { return (Index + 1).ToString(CultureInfo.CurrentCulture); } }
        public bool IsCurrent { get { return _isCurrent; } set { if (_isCurrent == value) return; _isCurrent = value; NotifyOfPropertyChange(); } }
    }
}
