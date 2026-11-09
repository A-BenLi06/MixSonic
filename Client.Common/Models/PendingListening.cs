namespace Client.Common.Models
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public sealed class PendingListen
    {
        public string OccurrenceId { get; set; }
        public string SongId { get; set; }
        public DateTime StartedUtc { get; set; }
    }

    public sealed class PendingListening
    {
        public string AccountKey { get; set; }
        public List<PendingListen> Entries { get; set; }
        public PendingListening() { Entries = new List<PendingListen>(); }

        public void Validate(string account)
        {
            if (AccountKey != account || Entries == null || Entries.Any(entry => entry == null ||
                string.IsNullOrWhiteSpace(entry.SongId) || string.IsNullOrWhiteSpace(entry.OccurrenceId) ||
                entry.StartedUtc < new DateTime(1970, 1, 1)) ||
                Entries.Select(entry => entry.OccurrenceId).Distinct().Count() != Entries.Count)
                throw new InvalidDataException("The saved listening records are invalid.");
        }
    }
}
