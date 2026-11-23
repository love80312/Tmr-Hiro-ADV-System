using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TmrHiroStudio.Core
{
    /// <summary>
    /// Tmr-Hiro SRP／VAR 文字轉換。
    /// SRP 指令分類使用完整 32-bit opcode（低 16 位 Type、高 16 位 Flags）。
    /// PAC 解包時已完成 SRP Data nibble swap；本類別不再提供第二層 Transform。
    /// </summary>
    internal static class ScriptTextCodec
    {
        private const string SrpTextHeaderV1 = "TMRHIRO-SRP-TEXT/1";
        private const string SrpTextHeaderV2 = "TMRHIRO-SRP-TEXT/2";
        private const string SrpTextHeaderV3 = "TMRHIRO-SRP-TEXT/3";
        private const string SrpTextHeaderV4 = "TMRHIRO-SRP-TEXT/4";
        private const string VarTextHeaderV1 = "TMRHIRO-VAR-TEXT/1";
        private const string VarTextHeaderV2 = "TMRHIRO-VAR-TEXT/2";
        private const string VarTextHeaderV3 = "TMRHIRO-VAR-TEXT/3";

        // 對應 GUI 的文字編碼選項。這是 SRP／VAR Data 編碼，不是 TXT 檔案本身的編碼。
        public static string SelectedExportEncoding = "CP932";

        private enum TextEncodingKind
        {
            Cp932,
            Gbk,
            Utf8,
            Raw
        }

        private enum LegacyTransformKind
        {
            None,
            NibbleSwap
        }

        private sealed class ParsedSrpText
        {
            public TextEncodingKind Encoding;
            public LegacyTransformKind LegacyTransform;
            public uint HeaderCount;
            public int OriginalInstructionCount;
            public bool HasCountMetadata;
            public List<SrpInstruction> Instructions = new List<SrpInstruction>();
        }

        private sealed class ScoreResult
        {
            public double Score;
            public int Characters;
        }

        public static string DecompileSrp(byte[] srpBytes)
        {
            return DecompileSrp(srpBytes, SelectedExportEncoding);
        }

        public static string DecompileSrp(byte[] srpBytes, string encodingName)
        {
            List<SrpInstruction> instructions = ScriptCodec.ReadSrpInstructions(srpBytes);
            List<byte[]> samples = instructions
                .Where(IsTextInstruction)
                .Where(item => item.Data.Length > 0)
                .Select(item => GetTextBytes(item))
                .Where(bytes => bytes.Length > 0)
                .ToList();

            TextEncodingKind encoding = SelectEncoding(samples, encodingName);
            uint headerCount = BinaryUtil.ReadUInt32(srpBytes, 0);

            StringBuilder output = new StringBuilder();
            output.Append(SrpTextHeaderV4).Append('\n');
            output.Append("Encoding: ").Append(EncodingName(encoding)).Append('\n');
            output.Append("HeaderCount: ").Append(headerCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            output.Append("ParsedCount: ").Append(instructions.Count.ToString(CultureInfo.InvariantCulture)).Append("\n\n");

            foreach (SrpInstruction instruction in instructions)
            {
                string dataText;
                if (!IsTextInstruction(instruction) || encoding == TextEncodingKind.Raw)
                {
                    dataText = EscapeBytesAsHex(instruction.Data);
                }
                else
                {
                    dataText = EscapeTextInstruction(instruction, encoding);
                }

                output.Append("Type: ").Append(instruction.Type.ToString(CultureInfo.InvariantCulture)).Append('\n');
                output.Append("Flags: ").Append(instruction.Flags.ToString(CultureInfo.InvariantCulture)).Append('\n');
                output.Append("Data: ").Append(dataText).Append("\n\n");
            }

            return output.ToString();
        }

        public static byte[] CompileSrp(string text)
        {
            ParsedSrpText parsed = ParseSrpText(text);
            if (parsed.Instructions.Count == 0)
                throw new InvalidDataException("SRP 文字檔沒有指令。");

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                // 沒有新增／刪除指令時保留來源 count；結構有變更才以實際指令數更新。
                uint outputCount = parsed.HasCountMetadata &&
                    parsed.Instructions.Count == parsed.OriginalInstructionCount
                    ? parsed.HeaderCount
                    : (uint)parsed.Instructions.Count;
                writer.Write(outputCount);

                foreach (SrpInstruction instruction in parsed.Instructions)
                {
                    if (instruction.Data == null)
                        throw new InvalidDataException("SRP 指令 Data 不可為 null。");
                    if (instruction.Data.Length > 32763)
                        throw new InvalidDataException("單一 SRP 指令資料超過 32763 位元組。");

                    byte[] storedData = ApplyLegacyTransform(instruction.Data, parsed.LegacyTransform);
                    writer.Write((ushort)(4 + storedData.Length));
                    writer.Write(instruction.Type);
                    writer.Write(instruction.Flags);
                    writer.Write(storedData);
                }

                byte[] result = stream.ToArray();
                // 只驗證長度與檔案邊界，不強制要求某一種 footer。
                ScriptCodec.ReadSrpInstructions(result);
                return result;
            }
        }

        public static string DecompileVar(byte[] data)
        {
            return DecompileVar(data, SelectedExportEncoding);
        }

        public static string DecompileVar(byte[] data, string encodingName)
        {
            List<byte[]> values = ReadVarValues(data);
            List<byte[]> samples = values
                .Where(value => value.Length > 0 && !IsEndMarker(value))
                .ToList();
            TextEncodingKind encoding = SelectEncoding(samples, encodingName);

            StringBuilder output = new StringBuilder();
            output.Append(VarTextHeaderV3).Append('\n');
            output.Append("Encoding: ").Append(EncodingName(encoding)).Append('\n');
            foreach (byte[] value in values)
                output.Append(EscapeBytes(value, encoding)).Append('\n');
            return output.ToString();
        }

        public static byte[] CompileVar(string text)
        {
            string normalized = NormalizeNewlines(text);
            string[] lines = normalized.Split('\n');
            bool escapedMode = false;
            TextEncodingKind encoding = TextEncodingKind.Cp932;
            int start = 0;

            if (lines.Length > 0 &&
                (string.Equals(lines[0].Trim(), VarTextHeaderV3, StringComparison.Ordinal) ||
                 string.Equals(lines[0].Trim(), VarTextHeaderV2, StringComparison.Ordinal)))
            {
                escapedMode = true;
                start = 1;
                if (start >= lines.Length || !lines[start].StartsWith("Encoding: ", StringComparison.Ordinal))
                    throw new InvalidDataException("VAR TXT 標頭缺少 Encoding。");
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
                byte[] bytes = escapedMode
                    ? UnescapeBytes(value, encoding)
                    : EncodeText(value, TextEncodingKind.Cp932);
                if (bytes.Length > 32767)
                    throw new InvalidDataException("VAR 單筆字串超過 32767 位元組。");
                encodedValues.Add(bytes);
            }

            if (!IsEndMarker(encodedValues[encodedValues.Count - 1]))
                throw new InvalidDataException("VAR 文字檔必須以 [END] 作為最後一筆。");

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                foreach (byte[] value in encodedValues)
                {
                    writer.Write((ushort)value.Length);
                    writer.Write(value);
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

        private static bool IsTextInstruction(SrpInstruction instruction)
        {
            // 上游 SExtractor / VNTextPatch 的判斷：先合併 Type + Flags 成 32-bit opcode。
            // 0x00150050：訊息；低 16 位為 0：旁白／一般文字；0x00140010：選項。
            uint opcode = instruction.Opcode;
            return opcode == 0x00150050u ||
                   (opcode & 0x0000ffffu) == 0u ||
                   opcode == 0x00140010u;
        }

        private static byte[] GetTextBytes(SrpInstruction instruction)
        {
            if (instruction.Opcode == 0x00140010u)
            {
                // 選項指令前 3 bytes 是版面／計數欄位；文字從其後開始。
                if (instruction.Data.Length <= 3) return Array.Empty<byte>();
                return instruction.Data.Skip(3).ToArray();
            }
            return instruction.Data;
        }

        private static string EscapeTextInstruction(SrpInstruction instruction, TextEncodingKind encoding)
        {
            if (instruction.Opcode == 0x00140010u)
            {
                if (instruction.Data.Length < 3)
                    return EscapeBytesAsHex(instruction.Data);
                byte[] prefix = instruction.Data.Take(3).ToArray();
                byte[] body = instruction.Data.Skip(3).ToArray();
                return EscapeBytesAsHex(prefix) + EscapeBytes(body, encoding);
            }
            return EscapeBytes(instruction.Data, encoding);
        }

        private static ParsedSrpText ParseSrpText(string text)
        {
            string normalized = NormalizeNewlines(text);
            string[] lines = normalized.Split('\n');
            int index = 0;
            bool escapedMode = false;
            bool legacyTextTypes = false;
            bool legacyType01Text = false;
            TextEncodingKind encoding = TextEncodingKind.Cp932;
            LegacyTransformKind transform = LegacyTransformKind.None;
            uint headerCount = 0;
            int originalInstructionCount = 0;
            bool hasCountMetadata = false;

            string firstLine = lines.Length > 0 ? lines[0].Trim() : string.Empty;
            if (firstLine == SrpTextHeaderV4)
            {
                escapedMode = true;
                index = 1;
                if (index >= lines.Length || !lines[index].StartsWith("Encoding: ", StringComparison.Ordinal))
                    throw new InvalidDataException("SRP TXT v4 缺少 Encoding 標頭。");
                encoding = ParseEncoding(lines[index].Substring(10));
                index++;
                if (index >= lines.Length || !lines[index].StartsWith("HeaderCount: ", StringComparison.Ordinal))
                    throw new InvalidDataException("SRP TXT v4 缺少 HeaderCount 標頭。");
                if (!uint.TryParse(lines[index].Substring(13).Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out headerCount))
                    throw new InvalidDataException("SRP TXT v4 HeaderCount 不是有效的 UInt32。");
                index++;
                if (index >= lines.Length || !lines[index].StartsWith("ParsedCount: ", StringComparison.Ordinal))
                    throw new InvalidDataException("SRP TXT v4 缺少 ParsedCount 標頭。");
                if (!int.TryParse(lines[index].Substring(13).Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out originalInstructionCount) || originalInstructionCount < 0)
                    throw new InvalidDataException("SRP TXT v4 ParsedCount 不是有效的非負整數。");
                hasCountMetadata = true;
                index++;
            }
            else if (firstLine == SrpTextHeaderV3 || firstLine == SrpTextHeaderV2)
            {
                // 相容舊版：Transform 是舊 TXT 顯示層設定，新版不再產生該欄位。
                escapedMode = true;
                // TXT v2／v3 是本工具先前版本產生的格式；當時以 Type 0／1
                // 作為舊文字分類，其餘 Type 2／3 Data 以 \xHH 保留。
                // 這是舊格式相容規則，不影響新版完整 opcode 分類。
                legacyTextTypes = false;
                legacyType01Text = true;
                index = 1;
                if (index >= lines.Length || !lines[index].StartsWith("Encoding: ", StringComparison.Ordinal))
                    throw new InvalidDataException("SRP TXT v2／v3 缺少 Encoding 標頭。");
                encoding = ParseEncoding(lines[index].Substring(10));
                index++;
                if (index >= lines.Length || !lines[index].StartsWith("Transform: ", StringComparison.Ordinal))
                    throw new InvalidDataException("SRP TXT v2／v3 缺少 Transform 標頭。");
                transform = ParseLegacyTransform(lines[index].Substring(11));
                index++;
                if (firstLine == SrpTextHeaderV3)
                {
                    if (index >= lines.Length || !lines[index].StartsWith("HeaderCount: ", StringComparison.Ordinal))
                        throw new InvalidDataException("SRP TXT v3 缺少 HeaderCount 標頭。");
                    if (!uint.TryParse(lines[index].Substring(13).Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out headerCount))
                        throw new InvalidDataException("SRP TXT v3 HeaderCount 不是有效的 UInt32。");
                    index++;
                    if (index >= lines.Length || !lines[index].StartsWith("ParsedCount: ", StringComparison.Ordinal))
                        throw new InvalidDataException("SRP TXT v3 缺少 ParsedCount 標頭。");
                    if (!int.TryParse(lines[index].Substring(13).Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out originalInstructionCount) || originalInstructionCount < 0)
                        throw new InvalidDataException("SRP TXT v3 ParsedCount 不是有效的非負整數。");
                    hasCountMetadata = true;
                    index++;
                }
            }
            else if (firstLine == SrpTextHeaderV1)
            {
                escapedMode = true;
                legacyTextTypes = true;
                index = 1;
                transform = LegacyTransformKind.None;
            }
            else
            {
                // 舊 croCKer TXT：來源程式已對 Data 做 nibble swap 顯示；回寫時需反向交換。
                legacyTextTypes = true;
                transform = LegacyTransformKind.NibbleSwap;
            }

            ParsedSrpText parsed = new ParsedSrpText
            {
                Encoding = encoding,
                LegacyTransform = transform,
                HeaderCount = headerCount,
                OriginalInstructionCount = originalInstructionCount,
                HasCountMetadata = hasCountMetadata,
            };

            while (index < lines.Length)
            {
                while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
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

                uint opcode = ((uint)(ushort)flags << 16) | (ushort)type;
                bool allowText = IsTextOpcode(opcode) ||
                    (legacyType01Text && (type == 0 || type == 1)) ||
                    (legacyTextTypes && (type == 2 || type == 3 || type == 0 || type == 1));
                byte[] payload;
                if (!allowText || encoding == TextEncodingKind.Raw)
                {
                    if (!IsHexByteSequence(dataText))
                        throw new InvalidDataException(
                            "SRP opcode 0x" + opcode.ToString("X8", CultureInfo.InvariantCulture) +
                            " 的 Data 按二進位保護處理，只接受連續的 \\xHH 位元組表示法。");
                    payload = UnescapeBytes(dataText, TextEncodingKind.Raw);
                }
                else
                {
                    payload = escapedMode
                        ? UnescapeBytes(dataText, encoding)
                        : EncodeText(dataText, TextEncodingKind.Cp932);
                }

                parsed.Instructions.Add(new SrpInstruction
                {
                    Type = type,
                    Flags = flags,
                    Data = payload
                });
                index++;
            }

            return parsed;
        }

        private static bool IsTextOpcode(uint opcode)
        {
            return opcode == 0x00150050u || (opcode & 0x0000ffffu) == 0u || opcode == 0x00140010u;
        }

        private static TextEncodingKind SelectEncoding(List<byte[]> samples, string encodingName)
        {
            string choice = (encodingName ?? "CP932").Trim().ToUpperInvariant();
            if (choice != "AUTO" && choice != "AUTODETECT" && choice != "AUTO_DETECT")
                return ParseEncoding(choice);
            if (samples.Count == 0) return TextEncodingKind.Cp932;

            ScoreResult cp932 = ScoreSamples(samples, TextEncodingKind.Cp932);
            ScoreResult gbk = ScoreSamples(samples, TextEncodingKind.Gbk);
            ScoreResult utf8 = ScoreSamples(samples, TextEncodingKind.Utf8);
            ScoreResult best = cp932;
            TextEncodingKind selected = TextEncodingKind.Cp932;
            if (gbk.Score > best.Score + 1.0) { best = gbk; selected = TextEncodingKind.Gbk; }
            if (utf8.Score > best.Score + 1.0) { best = utf8; selected = TextEncodingKind.Utf8; }
            if (best.Characters == 0 || best.Score < 0.5) return TextEncodingKind.Raw;
            return selected;
        }

        private static ScoreResult ScoreSamples(List<byte[]> samples, TextEncodingKind encoding)
        {
            ScoreResult result = new ScoreResult();
            foreach (byte[] sample in samples)
            {
                try
                {
                    string text = DecodeStrict(sample, encoding);
                    foreach (char ch in text)
                    {
                        result.Characters++;
                        if (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t')
                            result.Score -= 8.0;
                        else if ((ch >= '\u3040' && ch <= '\u30ff'))
                            result.Score += encoding == TextEncodingKind.Cp932 ? 5.0 : 1.0;
                        else if (ch >= '\u3400' && ch <= '\u9fff')
                            result.Score += 2.5;
                        else if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                            result.Score += 1.0;
                        else
                            result.Score += 0.5;
                    }
                }
                catch (DecoderFallbackException)
                {
                    result.Score -= Math.Max(2, sample.Length * 0.5);
                }
            }
            return result;
        }

        private static string DecodeStrict(byte[] bytes, TextEncodingKind encoding)
        {
            switch (encoding)
            {
                case TextEncodingKind.Cp932: return BinaryUtil.ShiftJisStrict.GetString(bytes);
                case TextEncodingKind.Gbk: return BinaryUtil.GbkStrict.GetString(bytes);
                case TextEncodingKind.Utf8: return BinaryUtil.Utf8Strict.GetString(bytes);
                default: return EscapeBytesAsHex(bytes);
            }
        }

        private static string EscapeBytes(byte[] bytes, TextEncodingKind encoding)
        {
            if (encoding == TextEncodingKind.Raw) return EscapeBytesAsHex(bytes);
            if (encoding == TextEncodingKind.Utf8)
            {
                try
                {
                    string decoded = BinaryUtil.Utf8Strict.GetString(bytes);
                    if (!BytesEqual(BinaryUtil.Utf8Strict.GetBytes(decoded), bytes)) return EscapeBytesAsHex(bytes);
                    return EscapeDecodedString(decoded);
                }
                catch (DecoderFallbackException) { return EscapeBytesAsHex(bytes); }
            }

            StringBuilder output = new StringBuilder(bytes.Length * 2);
            int position = 0;
            while (position < bytes.Length)
            {
                byte current = bytes[position];
                if (encoding == TextEncodingKind.Cp932 && position + 1 < bytes.Length)
                {
                    char hint;
                    if (PuaCharacterMap.TryDecodePair(current, bytes[position + 1], out hint))
                    {
                        output.Append("\\g{")
                            .Append(current.ToString("X2", CultureInfo.InvariantCulture))
                            .Append(bytes[position + 1].ToString("X2", CultureInfo.InvariantCulture))
                            .Append('|').Append(hint).Append('}');
                        position += 2;
                        continue;
                    }
                    if (PuaCharacterMap.IsEudcPair(current, bytes[position + 1]))
                    {
                        AppendHexByte(output, current);
                        AppendHexByte(output, bytes[position + 1]);
                        position += 2;
                        continue;
                    }
                }

                int byteCount = encoding == TextEncodingKind.Cp932
                    ? (IsCp932Lead(current) ? 2 : 1)
                    : (IsGbkLead(current) ? 2 : 1);
                if (position + byteCount > bytes.Length)
                {
                    AppendHexByte(output, current);
                    position++;
                    continue;
                }

                string decoded;
                try
                {
                    decoded = DecodeStrict(bytes.Skip(position).Take(byteCount).ToArray(), encoding);
                    byte[] encoded = EncodeText(decoded, encoding);
                    if (!BytesEqual(encoded, bytes.Skip(position).Take(byteCount).ToArray()))
                        throw new EncoderFallbackException("編碼往返不一致。");
                    if (encoding == TextEncodingKind.Gbk && decoded.Any(ch => ch >= '\ue000' && ch <= '\uf8ff'))
                        throw new EncoderFallbackException("GBK PUA 不使用通用字形顯示。");
                }
                catch (Exception ex) when (ex is DecoderFallbackException || ex is EncoderFallbackException)
                {
                    AppendHexByte(output, current);
                    position++;
                    continue;
                }

                output.Append(EscapeDecodedString(decoded));
                position += byteCount;
            }
            return output.ToString();
        }

        private static string EscapeDecodedString(string value)
        {
            StringBuilder output = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '\\': output.Append("\\\\"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\t': output.Append("\\t"); break;
                    case '\0': output.Append("\\0"); break;
                    default:
                        if (char.IsControl(ch))
                            output.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                        else
                            output.Append(ch);
                        break;
                }
            }
            return output.ToString();
        }

        private static string EscapeBytesAsHex(byte[] bytes)
        {
            StringBuilder output = new StringBuilder(bytes.Length * 4);
            foreach (byte value in bytes) AppendHexByte(output, value);
            return output.ToString();
        }

        private static void AppendHexByte(StringBuilder output, byte value)
        {
            output.Append("\\x").Append(value.ToString("X2", CultureInfo.InvariantCulture));
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
                    char ch = text[i];
                    if (ch != '\\')
                    {
                        normal.Append(ch);
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
                            byte hex;
                            if (!byte.TryParse(text.Substring(i + 1, 2), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out hex))
                                throw new InvalidDataException("\\x 跳脫的十六進位數字無效。");
                            output.WriteByte(hex);
                            i += 2;
                            break;
                        case 'u':
                            if (i + 4 >= text.Length)
                                throw new InvalidDataException("\\u 跳脫必須接四位十六進位數字。");
                            ushort unicode;
                            if (!ushort.TryParse(text.Substring(i + 1, 4), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out unicode))
                                throw new InvalidDataException("\\u 跳脫的十六進位數字無效。");
                            byte[] unicodeBytes = EncodeText(new string((char)unicode, 1), encoding);
                            output.Write(unicodeBytes, 0, unicodeBytes.Length);
                            i += 4;
                            break;
                        case 'g':
                            if (encoding != TextEncodingKind.Cp932)
                                throw new InvalidDataException("\\g EUDC 跳脫只適用於 CP932。");
                            if (i + 1 >= text.Length || text[i + 1] != '{')
                                throw new InvalidDataException("\\g EUDC 跳脫格式應為 \\g{F040} 或 \\g{F040|字形}。");
                            int close = text.IndexOf('}', i + 2);
                            if (close < 0) throw new InvalidDataException("\\g EUDC 跳脫缺少結尾大括號。");
                            string token = text.Substring(i + 2, close - i - 2).Split('|')[0];
                            if (token.Length != 4) throw new InvalidDataException("\\g EUDC 位元組必須是四位十六進位數字。");
                            byte lead, trail;
                            if (!byte.TryParse(token.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out lead) ||
                                !byte.TryParse(token.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out trail) ||
                                !PuaCharacterMap.IsEudcPair(lead, trail))
                                throw new InvalidDataException("\\g EUDC 跳脫不是有效的 CP932 私用造字位元組。");
                            output.WriteByte(lead);
                            output.WriteByte(trail);
                            i = close;
                            break;
                        default: throw new InvalidDataException("未知的跳脫序列：\\" + escape);
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
                case TextEncodingKind.Cp932: return BinaryUtil.ShiftJisStrict.GetBytes(text);
                case TextEncodingKind.Gbk: return BinaryUtil.GbkStrict.GetBytes(text);
                case TextEncodingKind.Utf8: return BinaryUtil.Utf8Strict.GetBytes(text);
                default: throw new InvalidDataException("RAW 模式不接受一般文字；請使用 \\xHH 表示原始位元組。");
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
                    throw new InvalidDataException("VAR 字串長度超出 Int16 可表示範圍。");
                if (length > data.Length - position)
                    throw new InvalidDataException("VAR 字串長度超出檔案範圍。");
                byte[] value = new byte[length];
                if (length > 0) Buffer.BlockCopy(data, position, value, 0, length);
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
            if (!foundEnd) throw new InvalidDataException("VAR 缺少 [END] 結尾標記。");
            return values;
        }

        private static bool IsEndMarker(byte[] value)
        {
            return value.Length == 5 && value[0] == 0x5b && value[1] == 0x45 &&
                   value[2] == 0x4e && value[3] == 0x44 && value[4] == 0x5d;
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
                        CultureInfo.InvariantCulture, out ignored)) return false;
            }
            return true;
        }

        private static byte[] ApplyLegacyTransform(byte[] data, LegacyTransformKind transform)
        {
            byte[] result = (byte[])data.Clone();
            if (transform == LegacyTransformKind.NibbleSwap)
                for (int i = 0; i < result.Length; i++) result[i] = BinaryUtil.SwapNibbles(result[i]);
            return result;
        }

        private static bool IsCp932Lead(byte value)
        {
            return (value >= 0x81 && value <= 0x9f) || (value >= 0xe0 && value <= 0xfc);
        }

        private static bool IsGbkLead(byte value)
        {
            return value >= 0x81 && value <= 0xfe;
        }

        private static string EncodingName(TextEncodingKind encoding)
        {
            switch (encoding)
            {
                case TextEncodingKind.Cp932: return "CP932";
                case TextEncodingKind.Gbk: return "GBK";
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
                case "SHIFT_JIS": return TextEncodingKind.Cp932;
                case "GBK":
                case "CP936":
                case "WINDOWS-936":
                case "GB2312": return TextEncodingKind.Gbk;
                case "UTF8":
                case "UTF-8": return TextEncodingKind.Utf8;
                case "RAW": return TextEncodingKind.Raw;
                default: throw new InvalidDataException("不支援的文字編碼標頭：" + value);
            }
        }

        private static LegacyTransformKind ParseLegacyTransform(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized == "NONE") return LegacyTransformKind.None;
            if (normalized == "NIBBLE_SWAP" || normalized == "NIBBLESWAP") return LegacyTransformKind.NibbleSwap;
            throw new InvalidDataException("不支援的舊版 SRP Transform：" + value);
        }

        private static short ParseInt16(string value, string field)
        {
            short parsed;
            if (!short.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                throw new InvalidDataException("SRP " + field + " 欄位不是有效的 -32768～32767 整數。");
            return parsed;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private static string NormalizeNewlines(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
