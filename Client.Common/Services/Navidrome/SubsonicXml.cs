namespace Client.Common.Services.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using System.Xml.Serialization;

    public static class SubsonicXml
    {
        public static readonly XNamespace Namespace = "http://subsonic.org/restapi";
        private static readonly Dictionary<string, XmlSerializer> Serializers = new Dictionary<string, XmlSerializer>();

        public static T Read<T>(XElement element) where T : class
        {
            if (element == null) return null;
            var key = typeof(T).FullName + ":" + element.Name;
            XmlSerializer serializer;
            lock (Serializers)
            {
                if (!Serializers.TryGetValue(key, out serializer))
                {
                    serializer = new XmlSerializer(typeof(T), new XmlRootAttribute(element.Name.LocalName) { Namespace = element.Name.NamespaceName });
                    Serializers.Add(key, serializer);
                }
            }
            using (var reader = element.CreateReader()) { return (T)serializer.Deserialize(reader); }
        }

        public static IList<T> List<T>(XElement element, string child) where T : class
        {
            return element == null ? new List<T>() : element.Elements(Namespace + child).Select(Read<T>).ToList();
        }

        public static string Text(XElement root, string child) { return (string)root.Element(Namespace + child); }
        public static string Attribute(XElement root, string name) { return (string)root.Attribute(name); }
        public static long Long(XElement root, string name) { return (long?)root.Attribute(name) ?? 0; }
        public static bool Bool(XElement root, string name) { return (bool?)root.Attribute(name) ?? false; }
    }
}
