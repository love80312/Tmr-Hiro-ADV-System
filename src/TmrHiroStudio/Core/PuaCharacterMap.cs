using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TmrHiroStudio.Core
{
    /// <summary>
    /// CP932 EUDC 字元提示表。
    /// TSV 欄位：CP932 位元組<TAB>CP932 解碼後的 PUA 碼位<TAB>可供人工辨識的字形提示。
    /// 注意：第三欄只作為文字匯出的顯示提示，絕不會把一般同字字元反向編碼成 EUDC。
    /// TXT 使用明確的 \g{F040|字形} 跳脫，避免一般漢字被誤轉成私用造字位元組。
    /// </summary>
    internal static class PuaCharacterMap
    {
        private const string FileName = "cp932-pua-map.tsv";
        private static readonly Lazy<MapData> Data = new Lazy<MapData>(Load, true);

        private sealed class MapData
        {
            public readonly Dictionary<int, char> BytesToHint = new Dictionary<int, char>();
        }

        public static bool TryDecodePair(byte first, byte second, out char displayHint)
        {
            int key = (first << 8) | second;
            return Data.Value.BytesToHint.TryGetValue(key, out displayHint);
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
                if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;

                string[] fields = line.Split('\t');
                if (fields.Length != 3)
                    throw MappingError(lineNumber, "每筆資料必須是三欄：位元組、PUA 碼位、字形提示。");

                string byteText = fields[0].Trim();
                string codePointText = fields[1].Trim();
                string hintText = fields[2];
                if (byteText.Length != 4 || codePointText.Length != 4)
                    throw MappingError(lineNumber, "位元組及 PUA 碼位都必須是四位十六進位數字。");

                byte lead;
                byte trail;
                int codePoint;
                if (!byte.TryParse(byteText.Substring(0, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out lead) ||
                    !byte.TryParse(byteText.Substring(2, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out trail))
                    throw MappingError(lineNumber, "CP932 位元組不是有效的十六進位數字。");

                if (!int.TryParse(codePointText, NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out codePoint))
                    throw MappingError(lineNumber, "PUA 碼位不是有效的十六進位數字。");

                if (!IsEudcPair(lead, trail))
                    throw MappingError(lineNumber, "位元組不在 CP932 EUDC 範圍 F040–F9FC。");
                if (codePoint < 0xe000 || codePoint > 0xe757)
                    throw MappingError(lineNumber, "PUA 碼位必須位於 CP932 EUDC 範圍 U+E000–U+E757。");
                if (hintText.Length != 1 || char.IsControl(hintText[0]) ||
                    char.IsWhiteSpace(hintText[0]) || hintText[0] == '\\' || hintText[0] == '|' || hintText[0] == '}' ||
                    char.IsSurrogate(hintText[0]))
                    throw MappingError(lineNumber, "字形提示必須是單一可列印 BMP 字元，且不能是反斜線、豎線或右大括號。");

                string decoded;
                try
                {
                    decoded = BinaryUtil.ShiftJisStrict.GetString(new[] { lead, trail });
                }
                catch (DecoderFallbackException ex)
                {
                    throw MappingError(lineNumber, "這組位元組無法由目前 CP932 解碼：" + ex.Message);
                }

                if (decoded.Length != 1 || decoded[0] != (char)codePoint)
                    throw MappingError(lineNumber,
                        "位元組 " + byteText.ToUpperInvariant() + " 在目前 CP932 對照結果不是 U+" +
                        codePoint.ToString("X4", CultureInfo.InvariantCulture) + "。");

                int key = (lead << 8) | trail;
                if (result.BytesToHint.ContainsKey(key))
                    throw MappingError(lineNumber, "CP932 位元組重複：" + byteText);

                // 不限制提示字元唯一：不同遊戲造字位元組可能使用相同的可讀字形。
                result.BytesToHint.Add(key, hintText[0]);
            }

            return result;
        }

        private static InvalidDataException MappingError(int zeroBasedLine, string message)
        {
            return new InvalidDataException(FileName + " 第 " + (zeroBasedLine + 1) + " 行：" + message);
        }
    }
}
