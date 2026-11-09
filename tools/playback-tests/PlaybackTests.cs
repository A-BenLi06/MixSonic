using System;
using System.Collections.Generic;
using System.Linq;
using Caliburn.Micro;
using Client.Common.EventAggregatorMessages;
using Client.Common.Models;
using Client.Common.Services;
using Client.Common.Results;
using Client.Common.Services.DataStructures.SubsonicService;
using Subsonic8.Listening;
using System.IO;
using System.Runtime.Serialization;
using System.Xml.Linq;

public static class PlaybackTests
{
    private static int _assertions;

    public static int Main()
    {
        try
        {
            Verify("context", ContextPlayback);
            Verify("repeat", RepeatModes);
            Verify("shuffle", ShuffleCycles);
            Verify("previous", PreviousHistory);
            Verify("mutation", QueueMutation);
            Verify("positions", QueuePositionAndReordering);
            Verify("import", ImportedQueueValidation);
            Verify("restore", RestoreState);
            Verify("history", RecentListening);
            Verify("favorites", FavouriteProtocol);
            Console.WriteLine("PASS: " + _assertions + " playback assertions against production sources.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception);
            return 1;
        }
    }

    private static void Verify(string group, Action action) { action(); Console.WriteLine("ND-CHECK:playback:" + group); }
    private static PlaylistManagementService Queue(int count)
    {
        var service = new PlaylistManagementService(new RecordingEvents());
        service.Items.AddRange(Enumerable.Range(0, count).Select(index => new PlaylistItem
        {
            Title = "Track " + index,
            Artist = "Fixture artist",
            Duration = 10,
            Uri = new Uri("http://localhost/fixture/" + index)
        }));
        return service;
    }

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new Exception(message);
    }

    private static void ImportedQueueValidation()
    {
        var service = Queue(2);
        service.StartPlayback(1);
        var original = service.Items.ToList();
        foreach (var invalid in new[]
        {
            new PlaylistItemCollection { null },
            new PlaylistItemCollection { new PlaylistItem { Title = "Missing URI" } },
            new PlaylistItemCollection { new PlaylistItem { Uri = new Uri("relative", UriKind.Relative) } }
        })
        {
            var rejected = false;
            try { service.LoadPlaylist(invalid); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid imported media must be rejected before changing playback.");
            Check(service.Items.SequenceEqual(original) && service.CurrentTrackNumber == 1 && service.IsPlaying,
                "Rejected imports must preserve the valid queue and current playback.");
        }
        service.LoadPlaylist(null);
        Check(service.Items.SequenceEqual(original), "Cancelled import must preserve the queue.");
        var replacement = Queue(3).Items;
        service.LoadPlaylist(replacement);
        Check(service.Items.SequenceEqual(replacement) && !service.IsPlaying,
            "Valid imports must replace in order without starting audio.");
    }

    private static void ContextPlayback()
    {
        var service = Queue(2);
        var context = Queue(4).Items.ToList();
        service.Handle(new PlayQueueMessage { Queue = context, StartIndex = 2 });
        Check(service.Items.SequenceEqual(context), "Context playback must replace, preserving order.");
        Check(service.CurrentTrackNumber == 2, "Context playback must start at the selected index.");
        service.Handle(new JumpToPreviousMessage());
        Check(service.CurrentTrackNumber == 1, "Previous must reach the preceding context entry.");
        service.Handle(new AddItemsMessage { Queue = context.Take(1).ToList() });
        Check(service.Items.Count == 5, "Add to queue must remain an append action.");
        service.Handle(new PlayQueueMessage { Queue = context, StartIndex = 4 });
        Check(service.Items.Count == 5, "Invalid context indices must leave the queue intact.");
        service.StartPlayback(999);
        Check(service.CurrentTrackNumber == 1, "Invalid playback indices must be harmless.");
    }

    private static void QueuePositionAndReordering()
    {
        var events = new RecordingEvents();
        var service = new PlaylistManagementService(events);
        var original = Queue(3).Items.ToList();
        var options = new PlaybackStartOptions { PositionMilliseconds = 12345 };
        service.Handle(new PlayQueueMessage { Queue = original, StartIndex = 1, Options = options });
        var start = events.Messages.OfType<StartPlaybackMessage>().Last();
        Check(ReferenceEquals(start.Item, original[1]) && ReferenceEquals(start.Options, options),
            "Server queue restoration must forward the selected occurrence and millisecond resume options.");
        var next = Queue(2).Items.ToList();
        service.InsertNext(next);
        Check(service.Items.SequenceEqual(new[] { original[0], original[1], next[0], next[1], original[2] }),
            "Play next must insert the complete selection in order immediately after the active item.");
        service.MoveItem(original[1], 4);
        Check(ReferenceEquals(service.CurrentItem, original[1]) && service.CurrentTrackNumber == 4 && service.IsPlaying,
            "Reordering the playing occurrence must preserve media and update its queue index.");
        var stopped = Queue(0);
        stopped.InsertNext(next);
        Check(stopped.Items.SequenceEqual(next) && !stopped.IsPlaying,
            "Play next into an empty queue must retain order without fabricating playback.");
    }

    private static void RepeatModes()
    {
        var service = Queue(3);
        service.StartPlayback(2);
        service.Handle(new PlayNextMessage());
        Check(!service.IsPlaying, "Repeat off must stop at the end.");
        service.Handle(new ToggleRepeatMessage());
        Check(service.RepeatMode == PlaybackRepeatMode.All, "First repeat toggle selects all.");
        service.StartPlayback(2);
        service.Handle(new PlayNextMessage());
        Check(service.CurrentTrackNumber == 0 && service.IsPlaying, "Repeat all must wrap to the first entry.");
        service.Handle(new ToggleRepeatMessage());
        Check(service.RepeatMode == PlaybackRepeatMode.One, "Second repeat toggle selects one.");
        service.StartPlayback(1);
        service.Handle(new PlayNextMessage());
        Check(service.CurrentTrackNumber == 1, "Repeat one must replay on media end.");
        service.Handle(new JumpToNextMessage());
        Check(service.CurrentTrackNumber == 2, "Manual next must escape repeat one.");
        service.Handle(new ToggleRepeatMessage());
        Check(service.RepeatMode == PlaybackRepeatMode.Off, "Third repeat toggle selects off.");
        var single = Queue(1);
        single.Handle(new ToggleShuffleMessage());
        single.Handle(new PlayNextMessage());
        single.Handle(new PlayNextMessage());
        Check(!single.IsPlaying, "A single shuffled item must stop with repeat off.");
        single.Handle(new ToggleRepeatMessage());
        single.Handle(new PlayNextMessage());
        Check(single.IsPlaying && single.CurrentTrackNumber == 0, "Single-item repeat all must replay.");
        var empty = Queue(0);
        empty.Handle(new PlayNextMessage());
        Check(!empty.IsPlaying && empty.CurrentItem == null, "Empty queue must remain stopped.");
    }

    private static void ShuffleCycles()
    {
        var service = Queue(11);
        service.StartPlayback(0);
        service.Handle(new ToggleShuffleMessage());
        var visited = new HashSet<int> { 0 };
        for (var index = 1; index < service.Items.Count; index++)
        {
            service.Handle(new PlayNextMessage());
            Check(visited.Add(service.CurrentTrackNumber), "Shuffle must not repeat an entry within its cycle.");
        }
        Check(visited.Contains(10) && visited.Count == 11, "Shuffle must include the last queue entry.");
        service.Handle(new PlayNextMessage());
        Check(!service.IsPlaying, "Shuffle with repeat off must finish after one cycle.");
        service.Handle(new ToggleRepeatMessage());
        for (var cycle = 0; cycle < 20; cycle++)
        {
            visited.Clear();
            for (var index = 0; index < service.Items.Count; index++)
            {
                var previous = service.CurrentTrackNumber;
                service.Handle(new PlayNextMessage());
                Check(previous != service.CurrentTrackNumber, "Shuffle must avoid immediate repeats at cycle boundaries.");
                Check(visited.Add(service.CurrentTrackNumber), "Every complete repeat-all cycle must contain each entry once.");
            }
            Check(visited.Count == 11, "Repeat-all shuffle must cover the whole queue.");
        }
    }

    private static void PreviousHistory()
    {
        var service = Queue(4);
        service.Handle(new ToggleShuffleMessage());
        service.StartPlayback(0);
        service.StartPlayback(1);
        service.StartPlayback(2);
        service.Handle(new JumpToPreviousMessage());
        Check(service.CurrentTrackNumber == 1, "Previous follows real listening order in shuffle.");
        service.Handle(new JumpToPreviousMessage());
        Check(service.CurrentTrackNumber == 0, "Repeated previous must not bounce between two tracks.");
    }

    private static void QueueMutation()
    {
        var service = Queue(4);
        service.StartPlayback(1);
        var removed = service.CurrentItem;
        service.Handle(new RemoveItemsMessage { Queue = new List<PlaylistItem> { removed } });
        Check(service.CurrentItem.Title == "Track 2" && service.IsPlaying, "Removing the active entry advances to its successor.");
        Check(removed.PlayingState == PlaylistItemState.NotPlaying, "Removed active entry must stop.");
        Check(service.PlaylistHistory.Count == 0, "Queue edits must discard obsolete navigation indices.");
        service.Clear();
        Check(service.CurrentItem == null && !service.IsPlaying && !service.HasElements, "Clear must reset the player and queue.");
    }

    private static void RestoreState()
    {
        var service = Queue(3);
        service.StartPlayback(1);
        service.Handle(new ToggleRepeatMessage());
        service.Handle(new ToggleRepeatMessage());
        service.Handle(new ToggleShuffleMessage());
        service.Pause();
        var restored = Queue(0);
        restored.SetStateFromString(service.GetStateAsString());
        Check(restored.CurrentTrackNumber == 1 && restored.Items.Count == 3, "Restore must retain the queue and selected index.");
        Check(restored.RepeatMode == PlaybackRepeatMode.One && restored.ShuffleOn, "Restore must retain repeat and shuffle separately.");
        Check(!restored.IsPlaying, "A stopped or paused saved queue must not auto-play.");
    }

    private static void RecentListening()
    {
        var history = new ListeningHistory();
        for (var index = 0; index < 125; index++)
            history.Record(new RecentTrack { SongId = "song-" + index, Title = "Fixture", PlayedAt = DateTimeOffset.Now });
        Check(history.Entries.Count == ListeningHistory.MaximumEntries, "Listening history must stay bounded.");
        Check(history.Entries[0].SongId == "song-124", "The latest listen must lead history.");
        history.Record(new RecentTrack { SongId = "song-50", Title = "Replayed", CoverArtId = "cover-50", PlayedAt = DateTimeOffset.Now });
        Check(history.Entries[0].SongId == "song-50" && history.Entries.Count(entry => entry.SongId == "song-50") == 1,
              "Replaying a song must move its single entry to the front.");
        history.Record(new RecentTrack());
        Check(history.Entries.Count == ListeningHistory.MaximumEntries, "Missing song IDs must not enter history.");
        using (var stream = new MemoryStream())
        {
            var serializer = new DataContractSerializer(typeof(ListeningHistory));
            serializer.WriteObject(stream, history);
            stream.Position = 0;
            var restored = (ListeningHistory)serializer.ReadObject(stream);
            Check(restored.Entries[0].Title == "Replayed" && restored.Entries[0].CoverArtId == "cover-50",
                  "Persisted recent metadata must round-trip independently of streaming URLs.");
        }
        var configuration = new SubsonicServiceConfiguration { BaseUrl = "http://localhost/Music/", Username = "listener", Password = "fixture" };
        var account = AccountIdentity.For(configuration);
        configuration.Password = "changed";
        Check(AccountIdentity.For(configuration) == account, "Changing a password must not lose listening history.");
        configuration.Username = "another listener";
        Check(AccountIdentity.For(configuration) != account, "Different users must have separate listening histories.");
        configuration.Username = "listener";
        configuration.BaseUrl = "http://localhost/music/";
        Check(AccountIdentity.For(configuration) != account, "Case-sensitive server paths must identify separate libraries.");
    }

    private static void FavouriteProtocol()
    {
        var configuration = new SubsonicServiceConfiguration { BaseUrl = "http://localhost/", Username = "fixture", Password = "fixture" };
        var star = new SetStarredResult(configuration, "song&1", true);
        Check(star.ResourcePath == "star.view" && star.RequestUrl.Contains("id=song%261"), "Star must encode the song ID as a query value.");
        star.HandleResponse(XDocument.Parse("<subsonic-response xmlns='http://subsonic.org/restapi' status='ok'/>"));
        Check(star.Result, "Only a successful server response confirms a favourite.");
        star.HandleResponse(XDocument.Parse("<subsonic-response xmlns='http://subsonic.org/restapi' status='failed'><error code='50'/></subsonic-response>"));
        Check(!star.Result, "Failed favourite responses must not confirm a state change.");
        var unstar = new SetStarredResult(configuration, "song-1", false);
        Check(unstar.ResourcePath == "unstar.view", "Removing a favourite must use unstar.");
    }

    private sealed class RecordingEvents : IEventAggregator
    {
        public readonly List<object> Messages = new List<object>();
        public bool HandlerExistsFor(Type messageType) { return true; }
        public void Subscribe(object subscriber) { }
        public void Unsubscribe(object subscriber) { }
        public void Publish(object message, Action<Action> marshal) { Messages.Add(message); }
    }
}
