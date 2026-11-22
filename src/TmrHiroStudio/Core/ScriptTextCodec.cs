using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TmrHiroStudio.Core
{
    /// <summary>
    /// SRP／VAR 文字轉換層。
    /// 以原 ScriptCodec 的 SRP 結構解析器為基礎，額外處理 CP932 EUDC 對照，
    /// 並將尚未確認語意的 SRP Type 80 視為不透明二進位資料。
    ///
    /// 注意：本轉換器接收的是已解除 PAC 腳本資料層轉換的 SRP。
    /// PAC 層的 nibble swap 由 PacCodec.Extract／Pack 成對處理；
    /// 此處不可再對 SRP Data 額外執行 nibble swap。
    /// </summary>
    internal static class ScriptTextCodec
    {
        private const string SrpTextHeader = "TMRHIRO-SRP-TEXT/1";
        private const string VarTextHeader = "TMRHIRO-VAR-TEXT/1";

        public static string DecompileSrp(byte[] srpBytes)
        {
            List<SrpInstruction> instructions = ScriptCodec.ReadSrpInstructions(srpBytes);
            StringBuilder text = new StringBuilder();
            text.AppendLine(SrpTextHeader);

            foreach (SrpInstruction instruction in instructions)
            {
                text.Append("Type: ").Append(instruction.Type.ToString(CultureInfo.InvariantCulture)).Append('\n');
                text.Append("Flags: ").Append(instruction.Flags.ToString(CultureInfo.InvariantCulture)).Append('\n');

                // PacCodec.Extract 已經解除 PAC 儲存層的 nibble swap。
                // 此處直接解讀目前 SRP Data；再交換一次會把 CP932 位元組破壞成控制碼。
                // croCKer 的參考分類將 Type 0／1／2／3 分別描述為文字、選項、圖像、音效，
                // 但並未定義 Type 80 或其他型別。本工具只對已知 0–3 嘗試 CP932，
                // Type 80／未知型別一律保留原始 bytes。
                string dataText = IsTextInstructionType(instruction.Type)
                    ? EscapeBytes(instruction.Data)
                    : EscapeBytesAsHex(instruction.Data);
                text.Append("Data: ").Append(dataText).Append("\n\n");
            }
            return text.ToString();
        }

        public static byte[] CompileSrp(string text)
        {
            List<SrpInstruction> instructions = ParseSrpText(text);
            if (instructions.Count == 0)
                throw new InvalidDataException("SRP 文字檔沒有指令。");
            if (instructions[instructions.Count - 1].Type != 16 ||
                instructions[instructions.Count - 1].Flags != 3 ||
                instructions[instructions.Count - 1].Data.Length != 0)
                throw new InvalidDataException("SRP 最後一筆必須是 Type 16／Flags 3／空 Data 結尾指令。");

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((uint)instructions.Count);
                foreach (SrpInstruction instruction in instructions)
                {
                    if (instruction.Data.Length > 32763)
                        throw new InvalidDataException("單一 SRP 指令資料超過 32763 位元組。");
                    writer.Write((ushort)(4 + instruction.Data.Length));
                    writer.Write(instruction.Type);
                    writer.Write(instruction.Flags);

                    // Data 已由 TXT 編碼回原始 CP932／位元組序列。
                    // 不在這裡交換 nibble；PacCodec.Pack 會負責 PAC 層的成對轉換。
                    writer.Write(instruction.Data);
                }

                byte[] result = stream.ToArray();
                // 交由原有解析器重新驗證長度、終止指令及檔案邊界。
                ScriptCodec.ReadSrpInstructions(result);
                return result;
            }
        }

        public static string DecompileVar(byte[] data)
        {
            List<byte[]> values = ReadVarValues(data);
            StringBuilder text = new StringBuilder();
            text.AppendLine(VarTextHeader);
            foreach (byte[] value in values)
                text.Append(EscapeBytes(value)).Append('\n');
            return text.ToString();
        }

        public static byte[] CompileVar(string text)
        {
            string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            bool escapedMode = lines.Length > 0 && lines[0].Trim() == VarTextHeader;
            int start = escapedMode ? 1 : 0;
            List<string> values = lines.Skip(start).ToList();
            while (values.Count > 0 && values[values.Count - 1].Length == 0)
                values.RemoveAt(values.Count - 1);

            if (values.Count == 0)
                throw new InvalidDataException("VAR 文字檔必須以 [END] 作為最後一筆。");

            List<byte[]> encodedValues = new List<byte[]>(values.Count);
            foreach (string value in values)
            {
                byte[] bytes = escapedMode ? UnescapeBytes(value) : EncodeText(value);
                if (bytes.Length > 32767)
                    throw new InvalidDataException("VAR 單筆字串超過 32767 位元組。");
                encodedValues.Add(bytes);
            }

            byte[] lastValue = encodedValues[encodedValues.Count - 1];
            if (!IsEndMarker(lastValue))
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

        private static List<SrpInstruction> ParseSrpText(string text)
        {
            string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            int index = 0;
            bool escapedMode = lines.Length > 0 && lines[0].Trim() == SrpTextHeader;
            if (escapedMode) index++;

            List<SrpInstruction> instructions = new List<SrpInstruction>();
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

                byte[] payload;
                if (!IsTextInstructionType(type))
                {
                    // Type 80 及所有未確認型別保持不透明：只接受連續的 \xHH，避免誤把二進位轉成文字。
                    if (!IsHexByteSequence(dataText))
                        throw new InvalidDataException("SRP Type " + type.ToString(CultureInfo.InvariantCulture) +
                            " 尚未確認資料語意；Data 只能使用連續的 \\xHH 位元組表示法。");
                    payload = UnescapeBytes(dataText);
                }
                else
                {
                    payload = escapedMode ? UnescapeBytes(dataText) : EncodeText(dataText);
                }

                instructions.Add(new SrpInstruction { Type = type, Flags = flags, Data = payload });
                index++;
            }
            return instructions;
        }

        private static bool IsTextInstructionType(short type)
        {
            // 依目前公開參考的暫定分類：0 文字、1 選項、2 圖像、3 音效。
            // 只表示這些類型的 Data 可以「嘗試」CP932；不代表所有遊戲都必然是文字。
            return type >= 0 && type <= 3;
        }

        private static short ParseInt16(string value, string field)
        {
            short parsed;
            if (!short.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                throw new InvalidDataException("SRP " + field + " 欄位不是有效的 -32768～32767 整數。");
            return parsed;
        }

        private static bool IsHexByteSequence(string value)
        {
            if (value.Length == 0) return true;
            if ((value.Length % 4) != 0) return false;
            for (int i = 0; i < value.Length; i += 4)
            {
                if (value[i] != '\\' || value[i + 1] != 'x') return false;
                byte ignored;
                if (!byte.TryParse(value.Substring(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ignored))
                    return false;
            }
            return true;
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

            if (!foundEnd)
                throw new InvalidDataException("VAR 缺少 [END] 結尾標記。");
            return values;
        }

        private static bool IsEndMarker(byte[] value)
        {
            if (value.Length != 5) return false;
            return value[0] == 0x5b && value[1] == 0x45 && value[2] == 0x4e &&
                   value[3] == 0x44 && value[4] == 0x5d;
        }

        private static string EscapeBytes(byte[] bytes)
        {
            StringBuilder result = new StringBuilder();
            int position = 0;
            while (position < bytes.Length)
            {
                byte current = bytes[position];

                if (position + 1 < bytes.Length)
                {
                    byte next = bytes[position + 1];
                    char replacement;
                    if (PuaCharacterMap.TryDecodePair(current, next, out replacement))
                    {
                        AppendEscapedCharacter(result, replacement);
                        position += 2;
                        continue;
                    }

                    // 未列入表格的 CP932 EUDC 不輸出成不可讀 PUA，也不猜測其字義。
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

                string decoded;
                try
                {
                    decoded = BinaryUtil.ShiftJisStrict.GetString(bytes, position, count);
                }
                catch (DecoderFallbackException)
                {
                    AppendHexByte(result, current);
                    position++;
                    continue;
                }

                // CP932 部分 byte sequence 可能存在非一對一映射。
                // 只有「解碼後再編碼」可還原成完全相同 bytes 時才輸出文字，否則保留為 hex。
                byte[] reencoded;
                try
                {
                    reencoded = BinaryUtil.ShiftJisStrict.GetBytes(decoded);
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
                    for (int i = 0; i < count; i++) AppendHexByte(result, bytes[position + i]);
                }
                else
                {
                    foreach (char c in decoded)
                        AppendEscapedCharacter(result, c);
                }
                position += count;
            }
            return result.ToString();
        }

        private static string EscapeBytesAsHex(byte[] bytes)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 4);
            foreach (byte value in bytes) AppendHexByte(result, value);
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

        private static byte[] UnescapeBytes(string text)
        {
            using (MemoryStream output = new MemoryStream())
            {
                StringBuilder normal = new StringBuilder();
                Action flush = () =>
                {
                    if (normal.Length == 0) return;
                    byte[] encoded = EncodeText(normal.ToString());
                    output.Write(encoded, 0, encoded.Length);
                    normal.Clear();
                };

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c != '\\')
                    {
                        normal.Append(c);
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
                            byte value;
                            if (!byte.TryParse(text.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                                throw new InvalidDataException("\\x 跳脫的十六進位數字無效。");
                            output.WriteByte(value);
                            i += 2;
                            break;
                        case 'u':
                            if (i + 4 >= text.Length)
                                throw new InvalidDataException("\\u 跳脫必須接四位十六進位數字。");
                            ushort unicodeValue;
                            if (!ushort.TryParse(text.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out unicodeValue))
                                throw new InvalidDataException("\\u 跳脫的十六進位數字無效。");
                            char unicode = (char)unicodeValue;
                            byte[] unicodeBytes = EncodeText(new string(unicode, 1));
                            output.Write(unicodeBytes, 0, unicodeBytes.Length);
                            i += 4;
                            break;
                        default:
                            throw new InvalidDataException("未知的跳脫序列：\\" + escape);
                    }
                }

                flush();
                return output.ToArray();
            }
        }

        private static byte[] EncodeText(string text)
        {
            using (MemoryStream output = new MemoryStream())
            {
                foreach (char value in text)
                {
                    byte[] mapped;
                    if (PuaCharacterMap.TryEncodeReplacement(value, out mapped))
                    {
                        output.Write(mapped, 0, mapped.Length);
                        continue;
                    }

                    if (value >= 0xe000 && value <= 0xe757)
                    {
                        throw new EncoderFallbackException(
                            "文字包含未建立對照的 CP932 私用字元 U+" + ((int)value).ToString("X4", CultureInfo.InvariantCulture) +
                            "。請先在 cp932-pua-map.tsv 建立對照，或以 \\xHH 表示原始位元組。");
                    }

                    byte[] encoded = BinaryUtil.ShiftJisStrict.GetBytes(new[] { value });
                    output.Write(encoded, 0, encoded.Length);
                }
                return output.ToArray();
            }
        }

        private static bool IsCp932Lead(byte value)
        {
            return (value >= 0x81 && value <= 0x9f) || (value >= 0xe0 && value <= 0xfc);
        }
    }
}

