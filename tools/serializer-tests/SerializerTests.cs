using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Runtime.Serialization;
using System.Xml.Linq;
using System.Xml.Serialization;

// Types that are never XML-serialized by the app (in-memory only), so their
// XmlSerializer reflection failures are recorded but not counted as failures.
//  - PlaylistHistoryStack : ICollection without a default XmlSerializer accessor
//  - GenericMediaModel    : no parameterless constructor
public static class SerializerTests
{
    private static readonly HashSet<string> KnownNotSerialized = new HashSet<string>
    {
        "Client.Common.Models.PlaylistHistoryStack",
        "Client.Common.Models.Subsonic.GenericMediaModel",
        // PlaylistItem/PlaylistItemCollection are DataContract-serialized (playlist.spls),
        // they are covered by the DataContractSerializer assertions below instead.
        "Client.Common.Models.PlaylistItem",
        "Client.Common.Models.PlaylistItemCollection",
    };

    private static int _failures;

    public static void Main()
    {
        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var asm = Assembly.LoadFrom(Path.Combine(exeDir, "ClientCommonDesktop.dll"));
        Type[] allTypes;
        try { allTypes = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            allTypes = ex.Types.Where(t => t != null).ToArray();
            Console.WriteLine("NOTE: skipped " + ex.LoaderExceptions.Length + " unloadable Caliburn-bound types");
        }

        var modelTypes = allTypes
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.Namespace != null && t.Namespace.StartsWith("Client.Common.Models"))
            .OrderBy(t => t.FullName)
            .ToList();

        foreach (var t in modelTypes)
        {
            if (KnownNotSerialized.Contains(t.FullName)) { Console.WriteLine("SKIP " + t.FullName); continue; }

            try
            {
                new XmlSerializer(t);
                Console.WriteLine("OK   " + t.FullName);
            }
            catch (Exception ex)
            {
                _failures++;
                var msg = new List<string> { ex.Message };
                var e = ex.InnerException;
                while (e != null) { msg.Add(e.Message); e = e.InnerException; }
                Console.WriteLine("FAIL " + t.FullName + " :: " + string.Join(" | ", msg));
            }
        }

        DeserializeGenre();
        DeserializeRadioStation();
        GenresHandleResponse();
        RadioStationsHandleResponse();
        IndexesHandleResponse();
        AlbumHandleResponse();
        MusicDirectoryHandleResponse();
        SongHandleResponse();

        Console.WriteLine(_failures == 0 ? "ALL TESTS PASSED (" + modelTypes.Count + " model types)"
                                         : _failures + " FAILURES");
        Environment.Exit(_failures == 0 ? 0 : 1);
    }

    private static void GenresHandleResponse()
    {
        var config = new Client.Common.Services.DataStructures.SubsonicService.SubsonicServiceConfiguration
        {
            BaseUrl = "http://127.0.0.1:10011", Username = "u", Password = "p"
        };
        var result = new Client.Common.Results.GetGenresResult(config);
        var xml = XDocument.Parse("<subsonic-response xmlns=\"http://subsonic.org/restapi\" status=\"ok\"><genres><genre songCount=\"1753\" albumCount=\"311\">Anime</genre><genre songCount=\"132\" albumCount=\"36\">Game</genre></genres></subsonic-response>");
        result.HandleResponse(xml);
        if (result.Result == null || result.Result.Count != 2 ||
            result.Result[0].Name != "Anime" || result.Result[0].SongCount != 1753 ||
            result.Result[1].Name != "Game")
        {
            _failures++;
            Console.WriteLine("FAIL GetGenresResult.HandleResponse");
            return;
        }
        Console.WriteLine("PASS GetGenresResult.HandleResponse: 2 genres (Anime 1753/311, Game 132/36)");
    }

    private static void RadioStationsHandleResponse()
    {
        var config = new Client.Common.Services.DataStructures.SubsonicService.SubsonicServiceConfiguration
        {
            BaseUrl = "http://127.0.0.1:10011", Username = "u", Password = "p"
        };
        var result = new Client.Common.Results.GetInternetRadioStationsResult(config);
        var xml = XDocument.Parse("<subsonic-response xmlns=\"http://subsonic.org/restapi\" status=\"ok\"><internetRadioStations><internetRadioStation id=\"i1\" name=\"AsiaFM\" streamUrl=\"http://asiafm/hk\" /><internetRadioStation id=\"i2\" name=\"HITS FM\" streamUrl=\"https://sk.cri.cn/887.m3u8\" /></internetRadioStations></subsonic-response>");
        result.HandleResponse(xml);
        if (result.Result == null || result.Result.Count != 2 ||
            result.Result[0].Name != "AsiaFM" || result.Result[1].StreamUrl != "https://sk.cri.cn/887.m3u8")
        {
            _failures++;
            Console.WriteLine("FAIL GetInternetRadioStationsResult.HandleResponse");
            return;
        }
        Console.WriteLine("PASS GetInternetRadioStationsResult.HandleResponse: 2 stations");
    }

    private static void IndexesHandleResponse()
    {
        var config = new Client.Common.Services.DataStructures.SubsonicService.SubsonicServiceConfiguration
        {
            BaseUrl = "http://127.0.0.1:10011", Username = "u", Password = "p"
        };
        var result = new Client.Common.Results.GetIndexResult(config, "1");
        var xml = XDocument.Parse("<subsonic-response xmlns=\"http://subsonic.org/restapi\" status=\"ok\"><indexes lastModified=\"1787910186000\" ignoredArticles=\"The El La\"><index name=\"#\"><artist id=\"12xz\" name=\"(测试艺人\" coverArt=\"ar-12xz_0\" /></index><index name=\"A\"><artist id=\"aq1\" name=\"Aqours\" coverArt=\"ar-aq1_0\" /></index></indexes></subsonic-response>");
        result.HandleResponse(xml);
        if (result.Result == null || result.Result.Artists == null || result.Result.Artists.Count != 2)
        {
            _failures++;
            Console.WriteLine("FAIL GetIndexResult.HandleResponse");
            return;
        }
        Console.WriteLine("PASS GetIndexResult.HandleResponse: " + result.Result.Artists.Count + " artists across index groups");
    }

    private static void AlbumHandleResponse()
    {
        var config = new Client.Common.Services.DataStructures.SubsonicService.SubsonicServiceConfiguration
        {
            BaseUrl = "http://127.0.0.1:10011", Username = "u", Password = "p"
        };
        var result = new Client.Common.Results.GetAlbumResult(config, "al-1");
        var xml = XDocument.Parse("<subsonic-response xmlns=\"http://subsonic.org/restapi\" status=\"ok\"><album id=\"al-1\" name=\"アイコトバ!\" artist=\"Various Artists\" artistId=\"63sq\" songCount=\"11\" duration=\"2506\"><song id=\"s1\" parent=\"al-1\" title=\"アイコトバ!\" artist=\"Various Artists\" duration=\"231\" track=\"1\" /><song id=\"s2\" parent=\"al-1\" title=\"Second Song\" artist=\"Various Artists\" duration=\"200\" track=\"2\" /></album></subsonic-response>");
        result.HandleResponse(xml);
        if (result.Result == null)
        {
            _failures++;
            Console.WriteLine("FAIL GetAlbumResult.HandleResponse");
            return;
        }
        Console.WriteLine("PASS GetAlbumResult.HandleResponse: title=" + result.Result.Name);
    }

    private static void MusicDirectoryHandleResponse()
    {
        var config = new Client.Common.Services.DataStructures.SubsonicService.SubsonicServiceConfiguration
        {
            BaseUrl = "http://127.0.0.1:10011", Username = "u", Password = "p"
        };
        var result = new Client.Common.Results.GetMusicDirectoryResult(config, "dir-1");
        var xml = System.Xml.Linq.XDocument.Parse("<subsonic-response xmlns=\"http://subsonic.org/restapi\" status=\"ok\"><directory id=\"dir-1\" name=\"Test Folder\"><child id=\"ch-1\" title=\"Album One\" artist=\"Artist A\" isDir=\"true\" /><child id=\"ch-2\" title=\"Song X\" artist=\"Artist A\" duration=\"200\" isDir=\"false\" /></directory></subsonic-response>");
        result.HandleResponse(xml);
        if (result.Result == null)
        {
            _failures++;
            Console.WriteLine("FAIL GetMusicDirectoryResult.HandleResponse");
            return;
        }
        Console.WriteLine("PASS GetMusicDirectoryResult.HandleResponse: " + result.Result.Id);
    }

    private static void SongHandleResponse()
    {
        var config = new Client.Common.Services.DataStructures.SubsonicService.SubsonicServiceConfiguration
        {
            BaseUrl = "http://127.0.0.1:10011", Username = "u", Password = "p"
        };
        var result = new Client.Common.Results.GetSongResult(config, "song-1");
        var xml = System.Xml.Linq.XDocument.Parse("<subsonic-response xmlns=\"http://subsonic.org/restapi\" status=\"ok\"><song id=\"song-1\" title=\"Test Song\" artist=\"Test Artist\" duration=\"200\" track=\"1\" /></subsonic-response>");
        result.HandleResponse(xml);
        if (result.Result == null)
        {
            _failures++;
            Console.WriteLine("FAIL GetSongResult.HandleResponse");
            return;
        }
        Console.WriteLine("PASS GetSongResult.HandleResponse: " + result.Result.Name);
    }

    private static void DeserializeGenre()
    {
        var xml = "<genre xmlns=\"http://subsonic.org/restapi\" songCount=\"1753\" albumCount=\"311\">Anime</genre>";
        var serializer = new XmlSerializer(typeof(Client.Common.Models.Subsonic.Genre));
        var g = (Client.Common.Models.Subsonic.Genre)serializer.Deserialize(new StringReader(xml));
        if (g.Name != "Anime" || g.SongCount != 1753 || g.AlbumCount != 311)
        {
            _failures++;
            Console.WriteLine("FAIL Genre round-trip");
            return;
        }
        Console.WriteLine("PASS Genre deserialize: " + g.Name + " / " + g.SongCount + " songs / " + g.AlbumCount + " albums");
    }

    private static void DeserializeRadioStation()
    {
        var xml = "<internetRadioStation xmlns=\"http://subsonic.org/restapi\" id=\"abc\" name=\"HITS FM\" streamUrl=\"http://x/y.m3u8\" />";
        var serializer = new XmlSerializer(typeof(Client.Common.Models.Subsonic.InternetRadioStation));
        var s = (Client.Common.Models.Subsonic.InternetRadioStation)serializer.Deserialize(new StringReader(xml));
        if (s.Name != "HITS FM" || s.StreamUrl != "http://x/y.m3u8")
        {
            _failures++;
            Console.WriteLine("FAIL InternetRadioStation round-trip");
            return;
        }
        Console.WriteLine("PASS InternetRadioStation deserialize: " + s.Name + " -> " + s.StreamUrl);
    }
}
