using System.Collections.Generic;
using System.Runtime.Serialization;

namespace TmrHiroStudio.Models
{
    [DataContract]
    public sealed class GrdMetadata
    {
        [DataMember(Order = 1)] public ushort Format { get; set; }
        [DataMember(Order = 2)] public ushort ScreenWidth { get; set; }
        [DataMember(Order = 3)] public ushort ScreenHeight { get; set; }
        [DataMember(Order = 4)] public ushort Bpp { get; set; }
        [DataMember(Order = 5)] public ushort Left { get; set; }
        [DataMember(Order = 6)] public ushort Right { get; set; }
        [DataMember(Order = 7)] public ushort Top { get; set; }
        [DataMember(Order = 8)] public ushort Bottom { get; set; }
        [DataMember(Order = 9)] public uint AlphaSize { get; set; }
        [DataMember(Order = 10)] public uint RSize { get; set; }
        [DataMember(Order = 11)] public uint GSize { get; set; }
        [DataMember(Order = 12)] public uint BSize { get; set; }

        public int Width { get { return System.Math.Abs((int)Right - Left); } }
        public int Height { get { return System.Math.Abs((int)Bottom - Top); } }
        public int PackType { get { return Format >> 8; } }
        public int Version { get { return Format & 0xff; } }
        public bool HasAlpha { get { return Bpp == 32 && AlphaSize > 0; } }
    }

    [DataContract]
    public sealed class PacManifest
    {
        [DataMember(Order = 1)] public int FormatVersion { get; set; }
        [DataMember(Order = 2)] public int NameLength { get; set; }
        [DataMember(Order = 3)] public string SourcePacName { get; set; } = string.Empty;
        [DataMember(Order = 4)] public List<PacManifestEntry> Entries { get; set; } = new List<PacManifestEntry>();
    }

    [DataContract]
    public sealed class PacManifestEntry
    {
        [DataMember(Order = 1)] public string FileName { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string OriginalNameBytes { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string Extension { get; set; } = string.Empty;
        [DataMember(Order = 4)] public bool IsScriptResource { get; set; }
    }
}
