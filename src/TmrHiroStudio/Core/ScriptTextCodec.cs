using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TmrHiroStudio.Core
{
    /// <summary>
    /// SRP／VAR 文字轉換器。
    /// SRP Data 的 nibble swap 可能因來源而不同：直接取得的 SRP 與經 PAC 解包後的 SRP
    /// 可能處於不同轉換階段。因此新 TXT 會比較 CP932／UTF-8 及 nibble swap／不交換四種候選，
    /// 將選定的 Encoding 與 Transform 寫入 TXT 標頭，回封時嚴格反向還原。
    /// 不再把 Type 2／3 或未知 Type 的二進位 Data 當成文字。
    /// </summary>
    internal static class ScriptTextCodec
    {
        private const string SrpTextHeaderV1 = "TMRHIRO-SRP-TEXT/1";
        private const string SrpTextHeaderV2 = "TMRHIRO-SRP-TEXT/2";
        private const string VarTextHeaderV1 = "TMRHIRO-VAR-TEXT/1";
        private const string VarTextHeaderV2 = "TMRHIRO-VAR-TEXT/2";
        private const double MinimumReadableScore = 0.70;
        private const double SwapSelectionMargin = 0.18;
        private const double EncodingSelectionMargin = 0.75;

        private enum TextEncodingKind
        {
            Cp932,
            Utf8,
            Raw
        }

        private enum TransformKind
        {
            None,
            NibbleSwap
        }

        private sealed class Profile
        {
            public TextEncodingKind Encoding;
            public TransformKind Transform;
            public double Score;
        }

        private sealed class ScoreResult
        {
            public double Total;
            public int Units;
            public double Average { get { return Units == 0 ? 0.0 : Total / Units; } }
        }

        private sealed class ParsedSrpText
        {
            public TextEncodingKind Encoding;
            public TransformKind Transform;
            public List<SrpInstruction> Instructions = new List<SrpInstruction>();
        }

        public static string DecompileSrp(byte[] srpBytes)
        {
            List<SrpInstruction> instructions = ScriptCodec.ReadSrpInstructions(srpBytes);
            Profile profile = DetectSrpProfile(instructions);

            StringBuilder text = new StringBuilder();
            text.Append(SrpTextHeaderV2).Append('\n');
            text.Append("Encoding: ").Append(EncodingName(profile.Encoding)).Append('\n');
            text.Append("Transform: ").Append(TransformName(profile.Transform)).Append('\n');
            text.Append('\n');

            foreach (SrpInstruction instruction in instructions)
            {
                byte[] displayData = ApplyTransform(instruction.Data, profile.Transform);
                bool isText = IsTextInstructionType(instruction.Type);
                string dataText = isText
                    ? EscapeBytes(displayData, profile.Encoding)
                    : EscapeBytesAsHex(displayData);

                text.Append("Type: ").Append(instruction.Type.ToString(CultureInfo.InvariantCulture)).Append('\n');
                text.Append("Flags: ").Append(instruction.Flags.ToString(CultureInfo.InvariantCulture)).Append('\n');
                text.Append("Data: ").Append(dataText).Append("\n\n");
            }

            return text.ToString();
        }

        public static byte[] CompileSrp(string text)
        {
            ParsedSrpText parsed = ParseSrpText(text);
            List<SrpInstruction> instructions = parsed.Instructions;
            if (instructions.Count == 0)
                throw new InvalidDataException("SRP 文字檔沒有指令。");

            SrpInstruction footer = instructions[instructions.Count - 1];
            if (footer.Type != 16 || footer.Flags != 3 || footer.Data.Length != 0)
                throw new InvalidDataException("SRP 最後一筆必須是 Type 16／Flags 3／空 Data 結尾指令。");

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((uint)instructions.Count);
                foreach (SrpInstruction instruction in instructions)
                {
                    if (instruction.Data.Length > 32763)
                        throw new InvalidDataException("單一 SRP 指令資料超過 32763 位元組。");

                    byte[] storedData = ApplyTransform(instruction.Data, parsed.Transform);
                    writer.Write((ushort)(4 + storedData.Length));
                    writer.Write(instruction.Type);
                    writer.Write(instruction.Flags);
                    writer.Write(storedData);
                }

                byte[] result = stream.ToArray();
                // 重新解析實際輸出，確保所有長度、指令邊界及唯一 footer 正確。
                ScriptCodec.ReadSrpInstructions(result);
                return result;
            }
        }

        public static string DecompileVar(byte[] data)
        {
            List<byte[]> values = ReadVarValues(data);
            Profile profile = DetectVarProfile(values);

            StringBuilder text = new StringBuilder();
            text.Append(VarTextHeaderV2).Append('\n');
            text.Append("Encoding: ").Append(EncodingName(profile.Encoding)).Append('\n');

            // 不插入分隔空行，否則會把合法的第一筆空字串和格式分隔符混淆。
            foreach (byte[] value in values)
                text.Append(EscapeBytes(value, profile.Encoding)).Append('\n');

            return text.ToString();
        }

        public static byte[] CompileVar(string text)
        {
            string normalized = NormalizeNewlines(text);
            string[] lines = normalized.Split('\n');
            bool escapedMode = false;
            TextEncodingKind encoding = TextEncodingKind.Cp932;
            int start = 0;

            if (lines.Length > 0 && string.Equals(lines[0].Trim(), VarTextHeaderV2, StringComparison.Ordinal))
            {
                escapedMode = true;
                start = 1;
                if (start >= lines.Length || !lines[start].StartsWith("Encoding: ", StringComparison.Ordinal))
                    throw new InvalidDataException("VAR TXT v2 缺少 Encoding 標頭。");
                encoding = ParseEncoding(lines[start].Substring(10));
                start++;
            }
            else if (lines.Length > 0 && string.Equals(lines[0].Trim(), VarTextHeaderV1, StringComparison.Ordinal))
            {
                escapedMode = true;
                start = 1;
                encoding = TextEncodingKind.Cp932;
            }

            List<string> values = lines.Skip(start).ToList();
            while (values.Count > 0 && values[values.Count - 1].Length == 0)
                values.RemoveAt(values.Count - 1);

            if (values.Count == 0)
                throw new InvalidDataException("VAR 文字檔必須以 [END] 作為最後一筆。");

            List<byte[]> encodedValues = new List<byte[]>(values.Count);
            foreach (string value in values)
            {
                byte[] bytes = escapedMode ? UnescapeBytes(value, encoding) : EncodeText(value, TextEncodingKind.Cp932);
                if (bytes.Length > 32767)
                    throw new InvalidDataException("VAR 單筆字串超過 32767 位元組。");
                encodedValues.Add(bytes);
            }

            if (!IsEndMarker(encodedValues[encodedValues.Count - 1]))
                throw new InvalidDataException("VAR 文字檔必須以 [END] 作為最後一筆。");

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                foreach (byte[] bytes in encodedValues)
                {
                    writer.Write((ushort)bytes.Length);
                    writer.Write(bytes);
                }

                byte[] result = stream.ToArray();
                ReadVarValues(result);
                return result;
            }
        }

        internal static bool IsValidVarBinary(byte[] data)
        {
            try
            {
                ReadVarValues(data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ParsedSrpText ParseSrpText(string text)
        {
            string normalized = NormalizeNewlines(text);
            string[] lines = normalized.Split('\n');
            int index = 0;
            bool escapedMode = false;
            bool legacyTextTypes = true;
            TextEncodingKind encoding = TextEncodingKind.Cp932;
            TransformKind transform = TransformKind.NibbleSwap;

            if (lines.Length > 0 && string.Equals(lines[0].Trim(), SrpTextHeaderV2, StringComparison.Ordinal))
            {
                escapedMode = true;
                legacyTextTypes = false;
                index = 1;
                if (index >= lines.Length || !lines[index].StartsWith("Encoding: ", StringComparison.Ordinal))
                    throw new InvalidDataException("SRP TXT v2 缺少 Encoding 標頭。");
                encoding = ParseEncoding(lines[index].Substring(10));
                index++;

                if (index >= lines.Length || !lines[index].StartsWith("Transform: ", StringComparison.Ordinal))
                    throw new InvalidDataException("SRP TXT v2 缺少 Transform 標頭。");
                transform = ParseTransform(lines[index].Substring(11));
                index++;
            }
            else if (lines.Length > 0 && string.Equals(lines[0].Trim(), SrpTextHeaderV1, StringComparison.Ordinal))
            {
                // v1 是本專案先前 ScriptTextCodec 的格式，當時沒有在文字層交換 nibble。
                escapedMode = true;
                legacyTextTypes = true;
                index = 1;
                encoding = TextEncodingKind.Cp932;
                transform = TransformKind.None;
            }
            // 沒有工具標頭的舊 croCKer TXT，其輸出文字已經解除 SRP Data nibble swap，
            // 因此回編時必須再交換一次，才能還原 SRP Data。

            ParsedSrpText parsed = new ParsedSrpText
            {
                Encoding = encoding,
                Transform = transform
            };

            while (index < lines.Length)
            {
                while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
                    index++;
                if (index >= lines.Length) break;

                if (!lines[index].StartsWith("Type: ", StringComparison.Ordinal))
                    throw new InvalidDataException("SRP 文字格式錯誤：預期 Type:。");
                short type = ParseInt16(lines[index].Substring(6), "Type");
                index++;

                if (index >= lines.Length || !lines[index].StartsWith("Flags: ", StringComparison.Ordinal))
                    throw new InvalidDataException("SRP 文字格式錯誤：預期 Flags:。");
                short flags = ParseInt16(lines[index].Substring(7), "Flags");
                index++;

                if (index >= lines.Length || !lines[index].StartsWith("Data: ", StringComparison.Ordinal))
                    throw new InvalidDataException("SRP 文字格式錯誤：預期 Data:。");
                string dataText = lines[index].Substring(6);

                byte[] payload;
                bool allowText = IsTextInstructionType(type) ||
                    (legacyTextTypes && (type == 2 || type == 3));
                if (!allowText || encoding == TextEncodingKind.Raw)
                {
                    if (!IsHexByteSequence(dataText))
                    {
                        throw new InvalidDataException(
                            "SRP Type " + type.ToString(CultureInfo.InvariantCulture) +
                            " 的 Data 按二進位保護處理，只接受連續的 \\xHH 位元組表示法。");
                    }
                    payload = UnescapeBytes(dataText, TextEncodingKind.Raw);
                }
                else
                {
                    payload = escapedMode
                        ? UnescapeBytes(dataText, encoding)
                        : EncodeText(dataText, TextEncodingKind.Cp932);
                }

                // TXT 內的 Data 是偵測時用來顯示／編輯的位元組；回編時反向套用同一轉換。
                payload = ApplyTransform(payload, transform);
                parsed.Instructions.Add(new SrpInstruction { Type = type, Flags = flags, Data = payload });
                index++;
            }

            return parsed;
        }

        private static Profile DetectSrpProfile(List<SrpInstruction> instructions)
        {
            List<byte[]> samples = instructions
                .Where(item => IsTextInstructionType(item.Type) && item.Data.Length > 0)
                .Select(item => item.Data)
                .ToList();

            return DetectProfile(samples, allowNibbleSwap: true);
        }

        private static Profile DetectVarProfile(List<byte[]> values)
        {
            List<byte[]> samples = values
                .Where(value => value.Length > 0 && !IsEndMarker(value))
                .ToList();
            return DetectProfile(samples, allowNibbleSwap: false);
        }

        private static Profile DetectProfile(List<byte[]> samples, bool allowNibbleSwap)
        {
            if (samples.Count == 0)
            {
                return new Profile
                {
                    Encoding = TextEncodingKind.Cp932,
                    Transform = TransformKind.None,
                    Score = 0
                };
            }

            Profile cp932None = ScoreProfile(samples, TextEncodingKind.Cp932, TransformKind.None);
            Profile utf8None = ScoreProfile(samples, TextEncodingKind.Utf8, TransformKind.None);
            Profile bestNoSwap = cp932None.Score >= utf8None.Score ? cp932None : utf8None;
            Profile best = bestNoSwap;

            if (allowNibbleSwap)
            {
                Profile cp932Swap = ScoreProfile(samples, TextEncodingKind.Cp932, TransformKind.NibbleSwap);
                Profile utf8Swap = ScoreProfile(samples, TextEncodingKind.Utf8, TransformKind.NibbleSwap);
                Profile bestSwap = cp932Swap.Score >= utf8Swap.Score ? cp932Swap : utf8Swap;
                if (bestSwap.Score > best.Score + SwapSelectionMargin)
                    best = bestSwap;
            }

            // CP932 是此引擎已知的傳統腳本編碼。若 UTF-8 優勢不明顯，選 CP932；
            // 尤其純 ASCII 內容在兩種編碼下位元組相同，不應無證據地標成 UTF-8。
            Profile cp932ForTransform = best.Transform == TransformKind.NibbleSwap
                ? ScoreProfile(samples, TextEncodingKind.Cp932, TransformKind.NibbleSwap)
                : cp932None;
            if (best.Encoding == TextEncodingKind.Utf8 &&
                cp932ForTransform.Score >= best.Score - EncodingSelectionMargin)
            {
                best = cp932ForTransform;
            }

            if (best.Score < MinimumReadableScore)
            {
                // 無法有把握判定文字編碼時，不產生看似可讀但可能錯誤的文字。
                // 改以原始 byte 表示；TXT 標頭會明確記錄 RAW。
                return new Profile
                {
                    Encoding = TextEncodingKind.Raw,
                    Transform = TransformKind.None,
                    Score = best.Score
                };
            }

            return best;
        }

        private static Profile ScoreProfile(
            List<byte[]> samples,
            TextEncodingKind encoding,
            TransformKind transform)
        {
            double total = 0;
            int units = 0;

            foreach (byte[] original in samples)
            {
                byte[] candidate = ApplyTransform(original, transform);
                ScoreResult result = encoding == TextEncodingKind.Cp932
                    ? ScoreCp932(candidate)
                    : ScoreUtf8(candidate);
                total += result.Total;
                units += result.Units;
            }

            return new Profile
            {
                Encoding = encoding,
                Transform = transform,
                Score = units == 0 ? 0 : total / units
            };
        }

        private static ScoreResult ScoreCp932(byte[] bytes)
        {
            ScoreResult result = new ScoreResult();
            int position = 0;

            while (position < bytes.Length)
            {
                byte current = bytes[position];

                if (position + 1 < bytes.Length && PuaCharacterMap.IsEudcPair(current, bytes[position + 1]))
                {
                    result.Total += 0.9;
                    result.Units++;
                    position += 2;
                    continue;
                }

                if (current < 0x80)
                {
                    if (current == 0x09 || current == 0x0a || current == 0x0d || current == 0x00)
                        result.Total += 0.0;
                    else if (current >= 0x20 && current <= 0x7e)
                        result.Total += 1.25;
                    else
                        // Type 0／1 Data 可能混有遊戲控制碼；這些控制碼會在 TXT 以 \uXXXX 保留，
                        // 不應因少量控制碼就讓整份日文腳本被誤判成 RAW。
                        result.Total -= 0.25;
                    result.Units++;
                    position++;
                    continue;
                }

                if (current >= 0xa1 && current <= 0xdf)
                {
                    try
                    {
                        string decoded = BinaryUtil.ShiftJisStrict.GetString(bytes, position, 1);
                        result.Total += ScoreCharacter(decoded[0]);
                    }
                    catch (DecoderFallbackException)
                    {
                        result.Total -= 8.0;
                    }
                    result.Units++;
                    position++;
                    continue;
                }

                if (IsCp932Lead(current))
                {
                    if (position + 1 < bytes.Length && IsCp932Trail(bytes[position + 1]))
                    {
                        try
                        {
                            string decoded = BinaryUtil.ShiftJisStrict.GetString(bytes, position, 2);
                            if (decoded.Length == 0)
                                result.Total -= 8.0;
                            else
                                foreach (char value in decoded)
                                    result.Total += ScoreCharacter(value);
                        }
                        catch (DecoderFallbackException)
                        {
                            result.Total -= 8.0;
                        }
                        result.Units++;
                        position += 2;
                        continue;
                    }

                    result.Total -= 8.0;
                    result.Units++;
                    position++;
                    continue;
                }

                result.Total -= 8.0;
                result.Units++;
                position++;
            }

            return result;
        }

        private static ScoreResult ScoreUtf8(byte[] bytes)
        {
            ScoreResult result = new ScoreResult();
            string decoded;
            try
            {
                decoded = BinaryUtil.Utf8Strict.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                result.Total = -8.0 * Math.Max(1, bytes.Length);
                result.Units = Math.Max(1, bytes.Length);
                return result;
            }

            foreach (char value in decoded)
            {
                result.Total += ScoreCharacter(value);
                result.Units++;
            }
            return result;
        }

        private static double ScoreCharacter(char value)
        {
            int code = value;
            if (code >= 0xe000 && code <= 0xf8ff) return 0.9; // EUDC／PUA：可保留，但尚無字形語意。
            if (code >= 0x3040 && code <= 0x309f) return 5.0; // Hiragana
            if (code >= 0x30a0 && code <= 0x30ff) return 4.5; // Katakana
            if (code >= 0x3400 && code <= 0x9fff) return 3.4; // CJK unified ideographs
            if (code >= 0xf900 && code <= 0xfaff) return 3.0; // CJK compatibility ideographs
            if (code >= 0xff61 && code <= 0xff9f) return 0.7; // Half-width Katakana
            if (char.IsControl(value)) return -8.0;
            if (char.IsWhiteSpace(value)) return 0.8;
            if (char.IsLetterOrDigit(value)) return 1.6;

            UnicodeCategory category = char.GetUnicodeCategory(value);
            if (category == UnicodeCategory.OtherPunctuation ||
                category == UnicodeCategory.DashPunctuation ||
                category == UnicodeCategory.OpenPunctuation ||
                category == UnicodeCategory.ClosePunctuation ||
                category == UnicodeCategory.InitialQuotePunctuation ||
                category == UnicodeCategory.FinalQuotePunctuation ||
                category == UnicodeCategory.OtherSymbol ||
                category == UnicodeCategory.MathSymbol ||
                category == UnicodeCategory.CurrencySymbol)
                return 1.2;

            return 0.3;
        }

        private static bool IsTextInstructionType(short type)
        {
            // croCKer 的參考註解把 Type 0／1 描述為遊戲文字／選項。
            // Type 2／3 是圖像／音效，必須保留為 hex，避免把二進位誤判成文字。
            return type == 0 || type == 1;
        }

        private static string EscapeBytes(byte[] bytes, TextEncodingKind encoding)
        {
            if (encoding == TextEncodingKind.Raw)
                return EscapeBytesAsHex(bytes);

            if (encoding == TextEncodingKind.Utf8)
            {
                string decoded;
                try
                {
                    decoded = BinaryUtil.Utf8Strict.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    return EscapeBytesAsHex(bytes);
                }

                StringBuilder utf8Result = new StringBuilder(decoded.Length);
                foreach (char value in decoded)
                    AppendEscapedCharacter(utf8Result, value);
                return utf8Result.ToString();
            }

            StringBuilder result = new StringBuilder();
            int position = 0;
            while (position < bytes.Length)
            {
                byte current = bytes[position];

                if (position + 1 < bytes.Length)
                {
                    byte next = bytes[position + 1];
                    char displayHint;
                    if (PuaCharacterMap.TryDecodePair(current, next, out displayHint))
                    {
                        // 字形只放在明確的跳脫 token 內；一般相同漢字不會因此被映射成 EUDC。
                        result.Append("\\g{")
                            .Append(current.ToString("X2", CultureInfo.InvariantCulture))
                            .Append(next.ToString("X2", CultureInfo.InvariantCulture))
                            .Append('|').Append(displayHint).Append('}');
                        position += 2;
                        continue;
                    }

                    if (PuaCharacterMap.IsEudcPair(current, next))
                    {
                        AppendHexByte(result, current);
                        AppendHexByte(result, next);
                        position += 2;
                        continue;
                    }
                }

                int count = IsCp932Lead(current) ? 2 : 1;
                if (count == 2 && position + 1 >= bytes.Length)
                {
                    AppendHexByte(result, current);
                    position++;
                    continue;
                }

                string decodedPart;
                try
                {
                    decodedPart = BinaryUtil.ShiftJisStrict.GetString(bytes, position, count);
                }
                catch (DecoderFallbackException)
                {
                    AppendHexByte(result, current);
                    position++;
                    continue;
                }

                byte[] reencoded;
                try
                {
                    reencoded = BinaryUtil.ShiftJisStrict.GetBytes(decodedPart);
                }
                catch (EncoderFallbackException)
                {
                    AppendHexByte(result, current);
                    if (count == 2) AppendHexByte(result, bytes[position + 1]);
                    position += count;
                    continue;
                }

                bool identical = reencoded.Length == count;
                for (int i = 0; identical && i < count; i++)
                    identical = reencoded[i] == bytes[position + i];

                if (!identical)
                {
                    for (int i = 0; i < count; i++)
                        AppendHexByte(result, bytes[position + i]);
                }
                else
                {
                    foreach (char value in decodedPart)
                        AppendEscapedCharacter(result, value);
                }

                position += count;
            }

            return result.ToString();
        }

        private static string EscapeBytesAsHex(byte[] bytes)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 4);
            foreach (byte value in bytes)
                AppendHexByte(result, value);
            return result.ToString();
        }

        private static void AppendHexByte(StringBuilder target, byte value)
        {
            target.Append("\\x").Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        private static void AppendEscapedCharacter(StringBuilder target, char value)
        {
            switch (value)
            {
                case '\\': target.Append("\\\\"); break;
                case '\r': target.Append("\\r"); break;
                case '\n': target.Append("\\n"); break;
                case '\t': target.Append("\\t"); break;
                case '\0': target.Append("\\0"); break;
                default:
                    if (char.IsControl(value))
                        target.Append("\\u").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        target.Append(value);
                    break;
            }
        }

        private static byte[] UnescapeBytes(string text, TextEncodingKind encoding)
        {
            using (MemoryStream output = new MemoryStream())
            {
                StringBuilder normal = new StringBuilder();
                Action flush = () =>
                {
                    if (normal.Length == 0) return;
                    byte[] encoded = EncodeText(normal.ToString(), encoding);
                    output.Write(encoded, 0, encoded.Length);
                    normal.Clear();
                };

                for (int i = 0; i < text.Length; i++)
                {
                    char value = text[i];
                    if (value != '\\')
                    {
                        normal.Append(value);
                        continue;
                    }
                    if (++i >= text.Length)
                        throw new InvalidDataException("文字檔最後有未完成的跳脫字元。");
                    flush();
                    char escape = text[i];
                    switch (escape)
                    {
                        case '\\': output.WriteByte((byte)'\\'); break;
                        case 'r': output.WriteByte(0x0d); break;
                        case 'n': output.WriteByte(0x0a); break;
                        case 't': output.WriteByte(0x09); break;
                        case '0': output.WriteByte(0x00); break;
                        case 'x':
                            if (i + 2 >= text.Length)
                                throw new InvalidDataException("\\x 跳脫必須接兩位十六進位數字。");
                            byte byteValue;
                            if (!byte.TryParse(text.Substring(i + 1, 2), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out byteValue))
                                throw new InvalidDataException("\\x 跳脫的十六進位數字無效。");
                            output.WriteByte(byteValue);
                            i += 2;
                            break;
                        case 'u':
                            if (i + 4 >= text.Length)
                                throw new InvalidDataException("\\u 跳脫必須接四位十六進位數字。");
                            ushort unicodeValue;
                            if (!ushort.TryParse(text.Substring(i + 1, 4), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out unicodeValue))
                                throw new InvalidDataException("\\u 跳脫的十六進位數字無效。");
                            byte[] unicodeBytes = EncodeText(new string((char)unicodeValue, 1), encoding);
                            output.Write(unicodeBytes, 0, unicodeBytes.Length);
                            i += 4;
                            break;
                        case 'g':
                            if (i + 1 >= text.Length || text[i + 1] != '{')
                                throw new InvalidDataException("\\g EUDC 跳脫格式應為 \\g{F040} 或 \\g{F040|字形}。");
                            int close = text.IndexOf('}', i + 2);
                            if (close < 0)
                                throw new InvalidDataException("\\g EUDC 跳脫缺少結尾大括號。");
                            string token = text.Substring(i + 2, close - i - 2);
                            string byteText = token.Split('|')[0];
                            if (byteText.Length != 4)
                                throw new InvalidDataException("\\g EUDC 跳脫格式應為 \\g{F040} 或 \\g{F040|字形}。");
                            byte lead;
                            byte trail;
                            if (!byte.TryParse(byteText.Substring(0, 2), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out lead) ||
                                !byte.TryParse(byteText.Substring(2, 2), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out trail) ||
                                !PuaCharacterMap.IsEudcPair(lead, trail))
                                throw new InvalidDataException("\\g EUDC 跳脫不是有效的 CP932 私用造字位元組。");
                            output.WriteByte(lead);
                            output.WriteByte(trail);
                            i = close;
                            break;
                        default:
                            throw new InvalidDataException("未知的跳脫序列：\\" + escape);
                    }
                }

                flush();
                return output.ToArray();
            }
        }

        private static byte[] EncodeText(string text, TextEncodingKind encoding)
        {
            switch (encoding)
            {
                case TextEncodingKind.Cp932:
                    return BinaryUtil.ShiftJisStrict.GetBytes(text);
                case TextEncodingKind.Utf8:
                    return BinaryUtil.Utf8Strict.GetBytes(text);
                default:
                    throw new InvalidDataException("RAW 模式不接受一般文字；請使用 \\xHH 表示原始位元組。");
            }
        }

        private static List<byte[]> ReadVarValues(byte[] data)
        {
            if (data == null || data.Length < 7)
                throw new InvalidDataException("VAR 檔案太短。");

            List<byte[]> values = new List<byte[]>();
            int position = 0;
            bool foundEnd = false;

            while (position < data.Length)
            {
                if (position > data.Length - 2)
                    throw new InvalidDataException("VAR 長度欄位截斷。");

                ushort length = BinaryUtil.ReadUInt16(data, position);
                position += 2;
                if (length > 32767)
                    throw new InvalidDataException("VAR 字串長度超出原始格式的 Int16 範圍。");
                if (length > data.Length - position)
                    throw new InvalidDataException("VAR 字串長度超出檔案範圍。");

                byte[] value = new byte[length];
                if (length > 0)
                    Buffer.BlockCopy(data, position, value, 0, length);
                position += length;
                values.Add(value);

                if (IsEndMarker(value))
                {
                    foundEnd = true;
                    if (position != data.Length)
                        throw new InvalidDataException("VAR [END] 後仍有多餘資料。");
                    break;
                }
            }

            if (!foundEnd)
                throw new InvalidDataException("VAR 缺少 [END] 結尾標記。");
            return values;
        }

        private static bool IsEndMarker(byte[] value)
        {
            return value.Length == 5 &&
                   value[0] == 0x5b && value[1] == 0x45 && value[2] == 0x4e &&
                   value[3] == 0x44 && value[4] == 0x5d;
        }

        private static bool IsHexByteSequence(string value)
        {
            if (value.Length == 0) return true;
            if ((value.Length % 4) != 0) return false;
            for (int i = 0; i < value.Length; i += 4)
            {
                if (value[i] != '\\' || value[i + 1] != 'x') return false;
                byte ignored;
                if (!byte.TryParse(value.Substring(i + 2, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out ignored))
                    return false;
            }
            return true;
        }

        private static short ParseInt16(string value, string field)
        {
            short parsed;
            if (!short.TryParse(value.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out parsed))
                throw new InvalidDataException("SRP " + field + " 欄位不是有效的 -32768～32767 整數。");
            return parsed;
        }

        private static string NormalizeNewlines(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static byte[] ApplyTransform(byte[] data, TransformKind transform)
        {
            if (transform == TransformKind.None || data.Length == 0)
                return (byte[])data.Clone();

            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = BinaryUtil.SwapNibbles(data[i]);
            return result;
        }

        private static bool IsCp932Lead(byte value)
        {
            return (value >= 0x81 && value <= 0x9f) || (value >= 0xe0 && value <= 0xfc);
        }

        private static bool IsCp932Trail(byte value)
        {
            return (value >= 0x40 && value <= 0x7e) || (value >= 0x80 && value <= 0xfc);
        }

        private static string EncodingName(TextEncodingKind encoding)
        {
            switch (encoding)
            {
                case TextEncodingKind.Cp932: return "CP932";
                case TextEncodingKind.Utf8: return "UTF8";
                default: return "RAW";
            }
        }

        private static TextEncodingKind ParseEncoding(string value)
        {
            switch ((value ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "CP932":
                case "SHIFT-JIS":
                case "SHIFT_JIS":
                    return TextEncodingKind.Cp932;
                case "UTF8":
                case "UTF-8":
                    return TextEncodingKind.Utf8;
                case "RAW":
                    return TextEncodingKind.Raw;
                default:
                    throw new InvalidDataException("不支援的文字編碼標頭：" + value);
            }
        }

        private static string TransformName(TransformKind transform)
        {
            return transform == TransformKind.NibbleSwap ? "NIBBLE_SWAP" : "NONE";
        }

        private static TransformKind ParseTransform(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized == "NONE") return TransformKind.None;
            if (normalized == "NIBBLE_SWAP" || normalized == "NIBBLESWAP")
                return TransformKind.NibbleSwap;
            throw new InvalidDataException("不支援的 SRP Data Transform：" + value);
        }
    }
}
