using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TmrHiroStudio.Core
{
    /// <summary>
    /// CP932 使用者自訂字元（EUDC / Private Use Area）對照表。
    /// 映射檔放在 EXE 同一資料夾，使用 UTF-8 TSV：Bytes<TAB>PrivateUse<TAB>Replacement。
    /// 未列入對照表的 EUDC 位元組一律保留為 \xHH，不猜測字義。
    /// </summary>
    internal static class PuaCharacterMap
    {
        private const string FileName = "cp932-pua-map.tsv";
        private static readonly Lazy<MapData> Data = new Lazy<MapData>(Load, true);

        private sealed class MapData
        {
            public readonly Dictionary<int, char> BytesToReplacement = new Dictionary<int, char>();
            public readonly Dictionary<char, byte[]> ReplacementToBytes = new Dictionary<char, byte[]>();
        }

        public static bool TryDecodePair(byte first, byte second, out char replacement)
        {
            int key = (first << 8) | second;
            return Data.Value.BytesToReplacement.TryGetValue(key, out replacement);
        }

        public static bool TryEncodeReplacement(char value, out byte[] bytes)
        {
            if (Data.Value.ReplacementToBytes.TryGetValue(value, out byte[] mapped))
            {
                bytes = mapped;
                return true;
            }
            bytes = Array.Empty<byte>();
            return false;
        }

        public static bool IsEudcPair(byte first, byte second)
        {
            bool validTrail = (second >= 0x40 && second <= 0x7e) ||
                              (second >= 0x80 && second <= 0xfc);
            return first >= 0xf0 && first <= 0xf9 && validTrail;
        }

        private static MapData Load()
        {
            MapData result = new MapData();
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            if (!File.Exists(path)) return result;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, new UTF8Encoding(false, true));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("無法以 UTF-8 讀取 " + FileName + "。", ex);
            }

            for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
            {
                string line = lines[lineNumber];
                if (string.IsNullOrWhiteSpace(line)) continue;
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

                string[] fields = line.Split('\t');
                if (fields.Length != 3)
                {
                    throw MappingError(lineNumber, "每筆資料必須是三欄：位元組、PUA 碼位、替代字元。");
                }

                string byteText = fields[0].Trim();
                string codePointText = fields[1].Trim();
                string replacementText = fields[2];
                if (byteText.Length != 4 || codePointText.Length != 4)
                    throw MappingError(lineNumber, "位元組及 PUA 碼位都必須是四位十六進位數字。");

                byte lead;
                byte trail;
                int codePoint;
                if (!byte.TryParse(byteText.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out lead) ||
                    !byte.TryParse(byteText.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out trail))
                    throw MappingError(lineNumber, "CP932 位元組不是有效的十六進位數字。");
                if (!int.TryParse(codePointText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out codePoint))
                    throw MappingError(lineNumber, "PUA 碼位不是有效的十六進位數字。");
                if (!IsEudcPair(lead, trail))
                    throw MappingError(lineNumber, "位元組不在 CP932 EUDC 範圍 F040–F9FC。");
                if (codePoint < 0xe000 || codePoint > 0xe757)
                    throw MappingError(lineNumber, "PUA 碼位必須位於 CP932 EUDC 範圍 U+E000–U+E757。");
                if (replacementText.Length != 1 || char.IsSurrogate(replacementText[0]) ||
                    char.IsControl(replacementText[0]) || char.IsWhiteSpace(replacementText[0]) || replacementText[0] == '\\')
                    throw MappingError(lineNumber, "替代字元必須是單一 BMP 可列印字元，且不能是反斜線（跳脫字元）。");

                char privateUse = (char)codePoint;
                char replacement = replacementText[0];
                string decoded;
                try
                {
                    decoded = BinaryUtil.ShiftJisStrict.GetString(new[] { lead, trail });
                }
                catch (DecoderFallbackException ex)
                {
                    throw MappingError(lineNumber, "這組位元組無法由目前 CP932 解碼：" + ex.Message);
                }
                if (decoded.Length != 1 || decoded[0] != privateUse)
                {
                    throw MappingError(lineNumber,
                        "位元組 " + byteText.ToUpperInvariant() + " 在目前 CP932 對照結果不是 U+" + codePoint.ToString("X4") + "。");
                }

                int key = (lead << 8) | trail;
                if (result.BytesToReplacement.ContainsKey(key))
                    throw MappingError(lineNumber, "CP932 位元組重複：" + byteText);
                if (result.ReplacementToBytes.ContainsKey(replacement))
                    throw MappingError(lineNumber, "替代字元重複：" + replacement);

                result.BytesToReplacement.Add(key, replacement);
                result.ReplacementToBytes.Add(replacement, new[] { lead, trail });
            }

            return result;
        }

        private static InvalidDataException MappingError(int zeroBasedLine, string message)
        {
            return new InvalidDataException(FileName + " 第 " + (zeroBasedLine + 1) + " 行：" + message);
        }
    }
}
