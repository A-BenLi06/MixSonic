namespace Client.Common.Models.Navidrome
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Json;
    using System.Text;
    using Client.Common.Services.Navidrome;

    [DataContract]
    public sealed class ClientPlaybackProfile
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "platform")] public string Platform { get; set; }
        [DataMember(Name = "maxAudioBitrate", EmitDefaultValue = false)] public long MaxAudioBitrate { get; set; }
        [DataMember(Name = "maxTranscodingAudioBitrate", EmitDefaultValue = false)] public long MaxTranscodingAudioBitrate { get; set; }
        [DataMember(Name = "directPlayProfiles")] public List<DirectPlayProfile> DirectPlayProfiles { get; set; }
        [DataMember(Name = "transcodingProfiles")] public List<TranscodingProfile> TranscodingProfiles { get; set; }
        [DataMember(Name = "codecProfiles")] public List<CodecPlaybackProfile> CodecProfiles { get; set; }

        public static ClientPlaybackProfile Windows81(int maximumKbps)
        {
            if (maximumKbps < 0) throw new ArgumentOutOfRangeException("maximumKbps");
            return new ClientPlaybackProfile
            {
                Name = SubsonicRequest.ClientName, Platform = "Windows 8.1 Store",
                MaxAudioBitrate = (long)maximumKbps * 1000, MaxTranscodingAudioBitrate = (long)maximumKbps * 1000,
                DirectPlayProfiles = new List<DirectPlayProfile>
                {
                    new DirectPlayProfile { Containers = new[] { "mp3" }, AudioCodecs = new[] { "mp3" }, Protocols = new[] { "http" }, MaxAudioChannels = 2 },
                    new DirectPlayProfile { Containers = new[] { "wav" }, AudioCodecs = new[] { "pcm" }, Protocols = new[] { "http" }, MaxAudioChannels = 2 },
                    new DirectPlayProfile { Containers = new[] { "mp4", "m4a", "aac" }, AudioCodecs = new[] { "aac" }, Protocols = new[] { "http" }, MaxAudioChannels = 2 },
                    new DirectPlayProfile { Containers = new[] { "asf", "wma" }, AudioCodecs = new[] { "wmav1", "wmav2" }, Protocols = new[] { "http" }, MaxAudioChannels = 2 }
                },
                TranscodingProfiles = new List<TranscodingProfile>
                { new TranscodingProfile { Container = "mp3", AudioCodec = "mp3", Protocol = "http", MaxAudioChannels = 2 } },
                CodecProfiles = new List<CodecPlaybackProfile> {
                    new CodecPlaybackProfile { Type = "AudioCodec", Name = "pcm", Limitations = new List<CodecLimitation> {
                        new CodecLimitation { Name = "audioBitdepth", Comparison = "LessThanEqual", Values = new[] { "16" }, Required = true },
                        new CodecLimitation { Name = "audioSamplerate", Comparison = "LessThanEqual", Values = new[] { "48000" }, Required = true }
                    } }
                }
            };
        }

        public string ToJson()
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(ClientPlaybackProfile)).WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray(), 0, (int)stream.Length);
            }
        }
    }

    [DataContract]
    public sealed class CodecPlaybackProfile
    {
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "limitations")] public List<CodecLimitation> Limitations { get; set; }
    }

    [DataContract]
    public sealed class CodecLimitation
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "comparison")] public string Comparison { get; set; }
        [DataMember(Name = "values")] public string[] Values { get; set; }
        [DataMember(Name = "required")] public bool Required { get; set; }
    }

    [DataContract]
    public sealed class DirectPlayProfile
    {
        [DataMember(Name = "containers")] public string[] Containers { get; set; }
        [DataMember(Name = "audioCodecs")] public string[] AudioCodecs { get; set; }
        [DataMember(Name = "protocols")] public string[] Protocols { get; set; }
        [DataMember(Name = "maxAudioChannels")] public int MaxAudioChannels { get; set; }
    }

    [DataContract]
    public sealed class TranscodingProfile
    {
        [DataMember(Name = "container")] public string Container { get; set; }
        [DataMember(Name = "audioCodec")] public string AudioCodec { get; set; }
        [DataMember(Name = "protocol")] public string Protocol { get; set; }
        [DataMember(Name = "maxAudioChannels")] public int MaxAudioChannels { get; set; }
    }

    public sealed class TranscodeDecision
    {
        public bool CanDirectPlay { get; set; }
        public bool CanTranscode { get; set; }
        public string TranscodeParams { get; set; }
        public string ErrorReason { get; set; }
        public IList<string> Reasons { get; set; }
        public AudioStreamDetails SourceStream { get; set; }
        public AudioStreamDetails TranscodeStream { get; set; }
    }

    public sealed class AudioStreamDetails
    {
        public string Protocol { get; set; }
        public string Container { get; set; }
        public string Codec { get; set; }
        public long Bitrate { get; set; }
        public int Channels { get; set; }
        public int SampleRate { get; set; }
        public int BitDepth { get; set; }
    }
}
