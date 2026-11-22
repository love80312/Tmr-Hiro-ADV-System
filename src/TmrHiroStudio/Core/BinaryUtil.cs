using System;
using System.IO;
using System.Text;

namespace TmrHiroStudio.Core
{
    internal static class BinaryUtil
    {
        public static readonly Encoding ShiftJis = Encoding.GetEncoding(932);
        public static readonly Encoding ShiftJisStrict = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        public static ushort ReadUInt16(byte[] data, int offset)
        {
            EnsureRange(data.Length, offset, 2, "UInt16");
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        public static uint ReadUInt32(byte[] data, int offset)
        {
            EnsureRange(data.Length, offset, 4, "UInt32");
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        public static int ReadInt32(byte[] data, int offset)
        {
            return unchecked((int)ReadUInt32(data, offset));
        }

        public static long ReadInt64(byte[] data, int offset)
        {
            EnsureRange(data.Length, offset, 8, "Int64");
            ulong value = 0;
            for (int i = 0; i < 8; i++) value |= (ulong)data[offset + i] << (8 * i);
            return unchecked((long)value);
        }

        public static void WriteUInt16(BinaryWriter writer, ushort value) { writer.Write(value); }
        public static void WriteUInt32(BinaryWriter writer, uint value) { writer.Write(value); }
        public static void WriteInt64(BinaryWriter writer, long value) { writer.Write(value); }

        public static void EnsureRange(int totalLength, int offset, int count, string field)
        {
            if (offset < 0 || count < 0 || offset > totalLength - count)
                throw new InvalidDataException("資料超出檔案範圍：" + field);
        }

        public static byte SwapNibbles(byte value)
        {
            return (byte)((value << 4) | (value >> 4));
        }

        public static string SafeLeafName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed";
            string cleaned = name.Replace('\0', ' ').Trim();
            cleaned = cleaned.Replace('/', '_').Replace('\\', '_');
            foreach (char c in Path.GetInvalidFileNameChars()) cleaned = cleaned.Replace(c, '_');
            cleaned = cleaned.TrimEnd(' ', '.');
            if (cleaned.Length == 0 || cleaned == "." || cleaned == "..") cleaned = "_";
            string stem = Path.GetFileNameWithoutExtension(cleaned);
            string upperStem = stem.ToUpperInvariant();
            bool reserved = upperStem == "CON" || upperStem == "PRN" || upperStem == "AUX" || upperStem == "NUL" ||
                (upperStem.Length == 4 && (upperStem.StartsWith("COM", StringComparison.Ordinal) || upperStem.StartsWith("LPT", StringComparison.Ordinal)) && upperStem[3] >= '1' && upperStem[3] <= '9');
            if (reserved) cleaned = "_" + cleaned;
            return cleaned;
        }

        public static string GetRelativePathCompat(string root, string path)
        {
            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string pathFull = Path.GetFullPath(path);
            Uri rootUri = new Uri(rootFull);
            Uri pathUri = new Uri(pathFull);
            string relative = Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString());
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }

        public static void WriteAtomically(string path, byte[] data)
        {
            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = fullPath + ".tmp";
            File.WriteAllBytes(temp, data);
            if (File.Exists(fullPath)) File.Replace(temp, fullPath, null);
            else File.Move(temp, fullPath);
        }
    }
}
