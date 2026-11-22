using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TmrHiroStudio.Core
{
    internal sealed class SrpInstruction
    {
        public short Type { get; set; }
        public short Flags { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    internal static class ScriptCodec
    {
        private const string SrpTextHeader = "TMRHIRO-SRP-TEXT/1";
        private const string VarTextHeader = "TMRHIRO-VAR-TEXT/1";

        public static bool LooksLikePacSrp(byte[] data)
        {
            return data != null && data.Length >= 10 && BinaryUtil.ReadUInt16(data, 4) == 6 &&
                   BinaryUtil.ReadUInt32(data, 6) == 0x00140050;
        }

        // PAC 儲存層會對每個 SRP 指令的 Data 區塊做一次 nibble swap。
        // 此轉換是自身反向的，因此同一函式可用於 PAC 解包及回封。
        public static byte[] TransformPacScriptLayer(byte[] input)
        {
            if (!LooksLikePacSrp(input)) throw new InvalidDataException("SRP 檔案缺少 Tmr-Hiro PAC 腳本識別標記。");
            byte[] result = (byte[])input.Clone();
            // 部分遊戲的 count 欄位已知可能不準確；只讀取欄位，實際解析以結尾指令及檔案長度為準。
            _ = BinaryUtil.ReadUInt32(result, 0);
            int position = 4;
            int parsedCount = 0;
            bool foundFooter = false;
            // 部分遊戲的 count 欄位可能不準確；以 Type 16 / Flags 3 的結尾指令及實際檔案長度為準。
            while (position < result.Length)
            {
                if (++parsedCount > 1000000 || position > result.Length - 6)
                    throw new InvalidDataException("SRP 指令標頭截斷或數量超出上限。");
                ushort instructionLength = BinaryUtil.ReadUInt16(result, position);
                short type = unchecked((short)BinaryUtil.ReadUInt16(result, position + 2));
                short flags = unchecked((short)BinaryUtil.ReadUInt16(result, position + 4));
                if (instructionLength < 4) throw new InvalidDataException("SRP 指令長度小於 4。");
                int dataLength = instructionLength - 4;
                position += 6;
                if (dataLength > result.Length - position) throw new InvalidDataException("SRP 指令資料超出檔案範圍。");
                for (int j = 0; j < dataLength; j++) result[position + j] = BinaryUtil.SwapNibbles(result[position + j]);
                position += dataLength;
                if (type == 16 && flags == 3 && dataLength == 0)
                {
                    foundFooter = true;
                    if (position != result.Length) throw new InvalidDataException("SRP 結尾指令後仍有資料。");
                    break;
                }
            }
            if (!foundFooter) throw new InvalidDataException("SRP 缺少 Type 16／Flags 3 結尾指令。");
            return result;
        }

        public static string DecompileSrp(byte[] srpBytes)
        {
            List<SrpInstruction> instructions = ReadSrpInstructions(srpBytes);
            StringBuilder text = new StringBuilder();
            text.AppendLine(SrpTextHeader);
            foreach (SrpInstruction instruction in instructions)
            {
                byte[] decoded = new byte[instruction.Data.Length];
                for (int i = 0; i < decoded.Length; i++) decoded[i] = BinaryUtil.SwapNibbles(instruction.Data[i]);
                text.Append("Type: ").Append(instruction.Type.ToString(CultureInfo.InvariantCulture)).Append('\n');
                text.Append("Flags: ").Append(instruction.Flags.ToString(CultureInfo.InvariantCulture)).Append('\n');
                text.Append("Data: ").Append(EscapeBytes(decoded)).Append("\n\n");
            }
            return text.ToString();
        }

        public static byte[] CompileSrp(string text)
        {
            List<SrpInstruction> instructions = ParseSrpText(text);
            if (instructions.Count == 0) throw new InvalidDataException("SRP 文字檔沒有指令。");
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((uint)instructions.Count);
                foreach (SrpInstruction instruction in instructions)
                {
                    if (instruction.Data.Length > 32763) throw new InvalidDataException("單一 SRP 指令資料超過 32763 位元組。");
                    writer.Write((ushort)(4 + instruction.Data.Length));
                    writer.Write(instruction.Type);
                    writer.Write(instruction.Flags);
                    byte[] encoded = new byte[instruction.Data.Length];
                    for (int i = 0; i < encoded.Length; i++) encoded[i] = BinaryUtil.SwapNibbles(instruction.Data[i]);
                    writer.Write(encoded);
                }
                byte[] result = stream.ToArray();
                // 用同一解析器回讀新檔，確認結尾位置、所有記錄邊界及唯一終止指令。
                ReadSrpInstructions(result);
                return result;
            }
        }

        public static List<SrpInstruction> ReadSrpInstructions(byte[] data)
        {
            if (data == null || data.Length < 10) throw new InvalidDataException("SRP 檔案太短。");
            ValidateSrpFooter(data);
            // 不以標頭 count 作為唯一依據：部分已知遊戲的 count 值與實際指令數不一致。
            _ = BinaryUtil.ReadUInt32(data, 0);
            List<SrpInstruction> instructions = new List<SrpInstruction>();
            int position = 4;
            bool foundFooter = false;
            while (position < data.Length)
            {
                if (instructions.Count >= 1000000 || position > data.Length - 6)
                    throw new InvalidDataException("SRP 指令標頭截斷或數量超出上限。");
                ushort length = BinaryUtil.ReadUInt16(data, position);
                short type = unchecked((short)BinaryUtil.ReadUInt16(data, position + 2));
                short flags = unchecked((short)BinaryUtil.ReadUInt16(data, position + 4));
                if (length < 4) throw new InvalidDataException("SRP 指令長度小於 4。");
                int dataLength = length - 4;
                position += 6;
                if (dataLength > data.Length - position) throw new InvalidDataException("SRP 指令內容超出檔案範圍。");
                byte[] payload = new byte[dataLength];
                if (dataLength > 0) Buffer.BlockCopy(data, position, payload, 0, dataLength);
                instructions.Add(new SrpInstruction { Type = type, Flags = flags, Data = payload });
                position += dataLength;
                if (type == 16 && flags == 3 && dataLength == 0)
                {
                    foundFooter = true;
                    if (position != data.Length) throw new InvalidDataException("SRP 結尾指令後仍有多餘資料。");
                    break;
                }
            }
            if (!foundFooter || instructions.Count == 0)
                throw new InvalidDataException("SRP 缺少 Type 16／Flags 3 結尾指令。");
            return instructions;
        }

        private static void ValidateSrpFooter(byte[] data)
        {
            if (data.Length < 4 || BinaryUtil.ReadUInt16(data, data.Length - 4) != 0x10 || BinaryUtil.ReadUInt16(data, data.Length - 2) != 0x03)
                throw new InvalidDataException("SRP 結尾標記不正確（預期 10 00 03 00）。");
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
                if (!lines[index].StartsWith("Type: ", StringComparison.Ordinal)) throw new InvalidDataException("SRP 文字格式錯誤：預期 Type:。");
                short type = ParseInt16(lines[index].Substring(6), "Type");
                index++;
                if (index >= lines.Length || !lines[index].StartsWith("Flags: ", StringComparison.Ordinal)) throw new InvalidDataException("SRP 文字格式錯誤：預期 Flags:。");
                short flags = ParseInt16(lines[index].Substring(7), "Flags");
                index++;
                if (index >= lines.Length || !lines[index].StartsWith("Data: ", StringComparison.Ordinal)) throw new InvalidDataException("SRP 文字格式錯誤：預期 Data:。");
                byte[] payload = escapedMode
                    ? UnescapeBytes(lines[index].Substring(6))
                    : BinaryUtil.ShiftJisStrict.GetBytes(lines[index].Substring(6));
                instructions.Add(new SrpInstruction { Type = type, Flags = flags, Data = payload });
                index++;
            }
            return instructions;
        }

        private static short ParseInt16(string value, string field)
        {
            short parsed;
            if (!short.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                throw new InvalidDataException("SRP " + field + " 欄位不是有效的 -32768～32767 整數。");
            return parsed;
        }

        public static string DecompileVar(byte[] data)
        {
            List<byte[]> values = ReadVarValues(data);
            StringBuilder text = new StringBuilder();
            text.AppendLine(VarTextHeader);
            foreach (byte[] value in values)
            {
                text.Append(EscapeBytes(value)).Append('\n');
            }
            return text.ToString();
        }

        public static byte[] CompileVar(string text)
        {
            string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            bool escapedMode = lines.Length > 0 && lines[0].Trim() == VarTextHeader;
            int start = escapedMode ? 1 : 0;
            List<string> values = lines.Skip(start).ToList();
            while (values.Count > 0 && values[values.Count - 1].Length == 0) values.RemoveAt(values.Count - 1);
            byte[] lastValue = values.Count == 0 ? Array.Empty<byte>() : (escapedMode
                ? UnescapeBytes(values[values.Count - 1])
                : BinaryUtil.ShiftJisStrict.GetBytes(values[values.Count - 1]));
            string lastText;
            try { lastText = BinaryUtil.ShiftJisStrict.GetString(lastValue); }
            catch (DecoderFallbackException) { lastText = string.Empty; }
            if (values.Count == 0 || lastValue.Length != 5 || lastText != "[END]")
                throw new InvalidDataException("VAR 文字檔必須以 [END] 作為最後一筆。");

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                foreach (string value in values)
                {
                    byte[] bytes = escapedMode ? UnescapeBytes(value) : BinaryUtil.ShiftJisStrict.GetBytes(value);
                    if (bytes.Length > 32767) throw new InvalidDataException("VAR 單筆字串超過 32767 位元組。");
                    writer.Write((ushort)bytes.Length);
                    writer.Write(bytes);
                }
                byte[] result = stream.ToArray();
                ReadVarValues(result);
                return result;
            }
        }

        private static List<byte[]> ReadVarValues(byte[] data)
        {
            if (data == null || data.Length < 7) throw new InvalidDataException("VAR 檔案太短。");
            List<byte[]> values = new List<byte[]>();
            int position = 0;
            bool foundEnd = false;
            while (position < data.Length)
            {
                if (position > data.Length - 2) throw new InvalidDataException("VAR 長度欄位截斷。");
                ushort length = BinaryUtil.ReadUInt16(data, position);
                position += 2;
                if (length > 32767) throw new InvalidDataException("VAR 字串長度超出原始格式的 Int16 範圍。");
                if (length > data.Length - position) throw new InvalidDataException("VAR 字串長度超出檔案範圍。");
                byte[] value = new byte[length];
                if (length > 0) Buffer.BlockCopy(data, position, value, 0, length);
                position += length;
                values.Add(value);
                bool isEnd = false;
                try { isEnd = BinaryUtil.ShiftJisStrict.GetString(value) == "[END]"; }
                catch (DecoderFallbackException) { }
                if (isEnd)
                {
                    foundEnd = true;
                    if (position != data.Length) throw new InvalidDataException("VAR [END] 後仍有多餘資料。");
                    break;
                }
            }
            if (!foundEnd) throw new InvalidDataException("VAR 缺少 [END] 結尾標記。");
            return values;
        }

        private static string EscapeBytes(byte[] bytes)
        {
            string decoded;
            try
            {
                decoded = BinaryUtil.ShiftJisStrict.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                StringBuilder hex = new StringBuilder();
                foreach (byte value in bytes) hex.Append("\\x").Append(value.ToString("X2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
            StringBuilder result = new StringBuilder();
            foreach (char c in decoded)
            {
                switch (c)
                {
                    case '\\': result.Append("\\\\"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\t': result.Append("\\t"); break;
                    case '\0': result.Append("\\0"); break;
                    default:
                        if (char.IsControl(c))
                        {
                            byte[] encoded = BinaryUtil.ShiftJis.GetBytes(new[] { c });
                            foreach (byte b in encoded) result.Append("\\x").Append(b.ToString("X2", CultureInfo.InvariantCulture));
                        }
                        else result.Append(c);
                        break;
                }
            }
            return result.ToString();
        }

        private static byte[] UnescapeBytes(string text)
        {
            using (MemoryStream output = new MemoryStream())
            {
                StringBuilder normal = new StringBuilder();
                Action flush = () =>
                {
                    if (normal.Length == 0) return;
                    byte[] encoded = BinaryUtil.ShiftJisStrict.GetBytes(normal.ToString());
                    output.Write(encoded, 0, encoded.Length);
                    normal.Clear();
                };
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c != '\\') { normal.Append(c); continue; }
                    if (++i >= text.Length) throw new InvalidDataException("文字檔最後有未完成的跳脫字元。");
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
                            if (i + 2 >= text.Length) throw new InvalidDataException("\\x 跳脫必須接兩位十六進位數字。");
                            byte value;
                            if (!byte.TryParse(text.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                                throw new InvalidDataException("\\x 跳脫的十六進位數字無效。");
                            output.WriteByte(value);
                            i += 2;
                            break;
                        default: throw new InvalidDataException("未知的跳脫序列：\\" + escape);
                    }
                }
                flush();
                return output.ToArray();
            }
        }
    }
}
