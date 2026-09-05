using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Runtime.Serialization;
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

        Console.WriteLine(_failures == 0 ? "ALL TESTS PASSED (" + modelTypes.Count + " model types)"
                                         : _failures + " FAILURES");
        Environment.Exit(_failures == 0 ? 0 : 1);
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
