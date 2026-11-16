namespace Mixdrome.Phone.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Json;
    using System.Text;

    // Queue positions distinguish repeated occurrences of the same media ID.
    internal sealed class AudioQueueState
    {
        private readonly Random random;
        private readonly List<int> order = new List<int>();
        private int orderPosition = -1;
        public AudioQueueState() : this(new Random()) { }
        internal AudioQueueState(Random random) { this.random = random; }
        public List<TrackInfo> Tracks { get; private set; } = new List<TrackInfo>();
        public int CurrentIndex { get; private set; } = -1;
        public bool Shuffle { get; private set; }
        public int Repeat { get; private set; }
        public bool CanNext { get { return orderPosition + 1 < order.Count || (Repeat == 1 && Tracks.Count > 0); } }

        public bool Replace(List<TrackInfo> tracks, int index)
        {
            if (tracks == null || tracks.Count == 0 || index < 0 || index >= tracks.Count) return false;
            foreach (var track in tracks)
            {
                Uri uri;
                if (track == null || !Uri.TryCreate(track.StreamUrl, UriKind.Absolute, out uri)
                    || (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "ms-appdata")) return false;
            }
            Tracks = new List<TrackInfo>(tracks);
            CurrentIndex = index;
            RebuildOrder();
            return true;
        }

        public void SetShuffle(bool value)
        {
            if (Shuffle == value) return;
            Shuffle = value;
            RebuildOrder();
        }

        public void SetRepeat(int value) { if (value >= 0 && value <= 2) Repeat = value; }

        public bool Select(int index)
        {
            if (index < 0 || index >= Tracks.Count) return false;
            CurrentIndex = index;
            orderPosition = order.IndexOf(index);
            return true;
        }

        public bool Next(bool mediaEnded)
        {
            if (Tracks.Count == 0) return false;
            if (mediaEnded && Repeat == 2) return true;
            if (orderPosition + 1 < order.Count)
            {
                CurrentIndex = order[++orderPosition];
                return true;
            }
            if (Repeat != 1) return false;
            var previous = CurrentIndex;
            RebuildOrder();
            if (Shuffle && order.Count > 1 && order[0] == previous)
            {
                var swap = order[0]; order[0] = order[1]; order[1] = swap;
            }
            orderPosition = 0;
            CurrentIndex = order[0];
            return true;
        }

        public bool Previous()
        {
            if (orderPosition <= 0) return false;
            CurrentIndex = order[--orderPosition];
            return true;
        }

        private void RebuildOrder()
        {
            order.Clear();
            for (var i = 0; i < Tracks.Count; i++) order.Add(i);
            if (Shuffle)
            {
                for (var i = order.Count - 1; i > 0; i--)
                {
                    var j = random.Next(i + 1);
                    var swap = order[i]; order[i] = order[j]; order[j] = swap;
                }
                var current = order.IndexOf(CurrentIndex);
                if (current >= 0) { var swap = order[0]; order[0] = order[current]; order[current] = swap; }
            }
            orderPosition = order.IndexOf(CurrentIndex);
        }
    }

    [DataContract]
    internal sealed class AudioQueueSnapshot
    {
        [DataMember] public List<TrackInfo> Tracks { get; set; }
        [DataMember] public string Version { get; set; }
    }

    internal static class AudioJson
    {
        public static string Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray(), 0, (int)stream.Length);
            }
        }

        public static T Deserialize<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }

    // Media messages can arrive after awaited file reads or after a background-task restart.
    internal sealed class AudioStateVersionGate
    {
        private readonly HashSet<string> retiredSessions = new HashSet<string>();
        private string session;
        private int sequence = -1;
        public bool Accept(string nextSession, int nextSequence)
        {
            if (string.IsNullOrEmpty(nextSession) || nextSequence < 0 || retiredSessions.Contains(nextSession)) return false;
            if (session != nextSession)
            {
                if (!string.IsNullOrEmpty(session)) retiredSessions.Add(session);
                session = nextSession;
                sequence = -1;
            }
            if (nextSequence <= sequence) return false;
            sequence = nextSequence;
            return true;
        }
        public bool IsCurrent(string expectedSession, int expectedSequence)
        {
            return session == expectedSession && sequence == expectedSequence;
        }
    }
}
