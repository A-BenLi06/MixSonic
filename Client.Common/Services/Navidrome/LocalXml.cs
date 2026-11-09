namespace Client.Common.Services.Navidrome
{
    using System.IO;
    using System.Text;
    using System.Xml;
    using System.Xml.Serialization;

    public static class LocalXml
    {
        public static string Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
                    new XmlSerializer(typeof(T)).Serialize(writer, value);
                var bytes = stream.ToArray();
                return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            }
        }

        public static T Deserialize<T>(string xml)
        {
            using (var text = new StringReader(xml))
            using (var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
        }
    }
}
