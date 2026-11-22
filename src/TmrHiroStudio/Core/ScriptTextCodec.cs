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
    /// 使用者可獨立指定 CP932、GBK／CP936、UTF-8 或 RAW；SRP Data 的 nibble swap
    /// 是另一個獨立選項，兩項設定都會記錄於 TXT 標頭並在回封時嚴格反向還原。
    /// AUTO 僅作輔助偵測，不能可靠區分 CP932 與 GBK 的共同漢字。
    /// Type 2／3 及未知 Type 的二進位 Data 不會被當成文字。
    /// </summary>
    internal static class ScriptTextCodec
    {
        private const string SrpTextHeaderV1 = "TMRHIRO-SRP-TEXT/1";
        private const string SrpTextHeaderV2 = "TMRHIRO-SRP-TEXT/2";
        private const string SrpTextHeaderV3 = "TMRHIRO-SRP-TEXT/3";
        private const string VarTextHeaderV1 = "TMRHIRO-VAR-TEXT/1";
        private const string VarTextHeaderV2 = "TMRHIRO-VAR-TEXT/2";
        private const string VarTextHeaderV3 = "TMRHIRO-VAR-TEXT/3";

        // 由 GUI 下拉選單設定。預設 CP932 + NONE，避免未確認前擅自進行第二次 nibble swap。
        // AUTO 僅保留為使用者可選的輔助偵測模式。
        public static string SelectedExportEncoding = "CP932";
        public static string SelectedExportTransform = "NONE";
        private const double MinimumReadableScore = 0.70;
        private const double SwapSelectionMargin = 0.18;
        private const double EncodingSelectionMargin = 0.75;

        private enum TextEncodingKind
        {
            Cp932,
            Gbk,
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
            public uint HeaderCount;
            public int OriginalInstructionCount;
            public bool HasCountMetadata;
            public List<SrpInstruction> Instructions = new List<SrpInstruction>();
        }

        public static string DecompileSrp(byte[] srpBytes)
        {
            return DecompileSrp(srpBytes, SelectedExportEncoding, SelectedExportTransform);
        }

        public static string DecompileSrp(byte[] srpBytes, string encodingName, string transformName)
        {
            List<SrpInstruction> instructions = ScriptCodec.ReadSrpInstructions(srpBytes);
            Profile profile = SelectProfile(
                instructions.Where(item => IsTextInstructionType(item.Type) && item.Data.Length > 0)
                    .Select(item => item.Data).ToList(),
                encodingName,
                transformName,
                allowNibbleSwap: true);

            uint originalHeaderCount = BinaryUtil.ReadUInt32(srpBytes, 0);
            StringBuilder text = new StringBuilder();
            text.Append(SrpTextHeaderV3).Append('\n');
            text.Append("Encoding: ").Append(EncodingName(profile.Encoding)).Append('\n');
            text.Append("Transform: ").Append(TransformName(profile.Transform)).Append('\n');
            text.Append("HeaderCount: ").Append(originalHeaderCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("ParsedCount: ").Append(instructions.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
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
                // 原始 count 欄位有已知樣本與實際指令數不一致的情況。
                // 只要指令筆數未變，就保留來源 count；若使用者新增／刪除指令，才更新為實際筆數。
                uint outputCount = parsed.HasCountMetadata &&
                    instructions.Count == parsed.OriginalInstructionCount
                    ? parsed.HeaderCount
                    : (uint)instructions.Count;
                writer.Write(outputCount);
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
            return DecompileVar(data, SelectedExportEncoding);
        }

        public static string DecompileVar(byte[] data, string encodingName)
        {
            List<byte[]> values = ReadVarValues(data);
            List<byte[]> samples = values.Where(value => value.Length > 0 && !IsEndMarker(value)).ToList();
            Profile profile = SelectProfile(samples, encodingName, "NONE", allowNibbleSwap: false);

            StringBuilder text = new StringBuilder();
            text.Append(VarTextHeaderV3).Append('\n');
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

            if (lines.Length > 0 &&
                (string.Equals(lines[0].Trim(), VarTextHeaderV3, StringComparison.Ordinal) ||
                 string.Equals(lines[0].Trim(), VarTextHeaderV2, StringComparison.Ordinal)))
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
            uint parsedHeaderCount = 0;
            int originalInstructionCount = 0;
            bool hasCountMetadata = false;

            bool version3 = lines.Length > 0 && string.Equals(lines[0].Trim(), SrpTextHeaderV3, StringComparison.Ordinal);
            if (version3 || (lines.Length > 0 && string.Equals(lines[0].Trim(), SrpTextHeaderV2, StringComparison.Ordinal)))
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
                if (version3)
                {
                    if (index >= lines.Length || !lines[index].StartsWith("HeaderCount: ", StringComparison.Ordinal))
                        throw new InvalidDataException("SRP TXT v3 缺少 HeaderCount 標頭。");
                    uint headerCount;
                    if (!uint.TryParse(lines[index].Substring(13).Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out headerCount))
                        throw new InvalidDataException("SRP TXT v3 HeaderCount 不是有效的 UInt32。");
                    parsedHeaderCount = headerCount;
                    index++;
                    if (index >= lines.Length || !lines[index].StartsWith("ParsedCount: ", StringComparison.Ordinal))
                        throw new InvalidDataException("SRP TXT v3 缺少 ParsedCount 標頭。");
                    int parsedCount;
                    if (!int.TryParse(lines[index].Substring(13).Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out parsedCount) || parsedCount < 0)
                        throw new InvalidDataException("SRP TXT v3 ParsedCount 不是有效的非負整數。");
                    originalInstructionCount = parsedCount;
                    hasCountMetadata = true;
                    index++;
                }
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
                Transform = transform,
                HeaderCount = parsedHeaderCount,
                OriginalInstructionCount = originalInstructionCount,
                HasCountMetadata = hasCountMetadata
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

        private static Profile SelectProfile(
            List<byte[]> samples,
            string encodingName,
            string transformName,
            bool allowNibbleSwap)
        {
            string encodingChoice = (encodingName ?? "CP932").Trim().ToUpperInvariant();
            string transformChoice = (transformName ?? "NONE").Trim().ToUpperInvariant();

            if (encodingChoice == "AUTO" || encodingChoice == "AUTODETECT" || encodingChoice == "AUTO_DETECT")
            {
                Profile detected = DetectProfile(samples, allowNibbleSwap && IsAutoTransform(transformChoice));
                if (!IsAutoTransform(transformChoice))
                {
                    TransformKind fixedTransform = ParseTransform(transformChoice);
                    detected = DetectBestEncoding(samples, fixedTransform);
                    detected.Transform = fixedTransform;
                }
                return detected;
            }

            TextEncodingKind fixedEncoding = ParseEncoding(encodingChoice);
            TransformKind transform = IsAutoTransform(transformChoice)
                ? DetectBestTransform(samples, fixedEncoding, allowNibbleSwap)
                : ParseTransform(transformChoice);
            return new Profile { Encoding = fixedEncoding, Transform = transform, Score = 1.0 };
        }

        private static bool IsAutoTransform(string value)
        {
            return value == "AUTO" || value == "AUTODETECT" || value == "AUTO_DETECT";
        }

        private static Profile DetectProfile(List<byte[]> samples, bool allowNibbleSwap)
        {
            if (samples.Count == 0)
                return new Profile { Encoding = TextEncodingKind.Cp932, Transform = TransformKind.None, Score = 0 };

            Profile none = DetectBestEncoding(samples, TransformKind.None);
            if (!allowNibbleSwap) return none;

            Profile swapped = DetectBestEncoding(samples, TransformKind.NibbleSwap);
            return swapped.Score > none.Score + SwapSelectionMargin ? swapped : none;
        }

        private static Profile DetectBestEncoding(List<byte[]> samples, TransformKind transform)
        {
            if (samples.Count == 0)
                return new Profile { Encoding = TextEncodingKind.Cp932, Transform = transform, Score = 0 };

            Profile best = ScoreProfile(samples, TextEncodingKind.Cp932, transform);
            Profile gbk = ScoreProfile(samples, TextEncodingKind.Gbk, transform);
            Profile utf8 = ScoreProfile(samples, TextEncodingKind.Utf8, transform);

            // 日文 CP932 的假名特徵分數較高；GBK 與 CP932 無法由共同漢字可靠區分時，
            // 保留 CP932 為 Auto 的相容性預設。簡體中文請明確選 GBK/CP936。
            if (gbk.Score > best.Score + EncodingSelectionMargin) best = gbk;
            if (utf8.Score > best.Score + EncodingSelectionMargin) best = utf8;

            if (best.Score < MinimumReadableScore)
                return new Profile { Encoding = TextEncodingKind.Raw, Transform = transform, Score = best.Score };
            best.Transform = transform;
            return best;
        }

        private static TransformKind DetectBestTransform(
            List<byte[]> samples, TextEncodingKind encoding, bool allowNibbleSwap)
        {
            if (!allowNibbleSwap || samples.Count == 0 || encoding == TextEncodingKind.Raw)
                return TransformKind.None;
            Profile none = ScoreProfile(samples, encoding, TransformKind.None);
            Profile swapped = ScoreProfile(samples, encoding, TransformKind.NibbleSwap);
            return swapped.Score > none.Score + SwapSelectionMargin
                ? TransformKind.NibbleSwap
                : TransformKind.None;
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
                ScoreResult result;
                if (encoding == TextEncodingKind.Cp932) result = ScoreCp932(candidate);
                else if (encoding == TextEncodingKind.Gbk) result = ScoreGbk(candidate);
                else result = ScoreUtf8(candidate);
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

        private static ScoreResult ScoreGbk(byte[] bytes)
        {
            ScoreResult result = new ScoreResult();
            int position = 0;
            while (position < bytes.Length)
            {
                byte current = bytes[position];
                if (current < 0x80)
                {
                    result.Total += ScoreAsciiByte(current);
                    result.Units++;
                    position++;
                    continue;
                }

                if (current == 0x80)
                {
                    try
                    {
                        string one = BinaryUtil.GbkStrict.GetString(bytes, position, 1);
                        result.Total += one.Length == 0 ? -8.0 : ScoreCharacter(one[0]);
                    }
                    catch (DecoderFallbackException) { result.Total -= 8.0; }
                    result.Units++;
                    position++;
                    continue;
                }

                if (current >= 0x81 && current <= 0xfe && position + 1 < bytes.Length &&
                    IsGbkTrail(bytes[position + 1]))
                {
                    try
                    {
                        string pair = BinaryUtil.GbkStrict.GetString(bytes, position, 2);
                        if (pair.Length == 0) result.Total -= 8.0;
                        else foreach (char ch in pair) result.Total += ScoreCharacter(ch);
                    }
                    catch (DecoderFallbackException) { result.Total -= 8.0; }
                    result.Units++;
                    position += 2;
                    continue;
                }

                result.Total -= 8.0;
                result.Units++;
                position++;
            }
            return result;
        }

        private static double ScoreAsciiByte(byte value)
        {
            if (value == 0x09 || value == 0x0a || value == 0x0d || value == 0x00) return 0.0;
            if (value >= 0x20 && value <= 0x7e) return 1.25;
            return -0.25;
        }

        private static bool IsGbkTrail(byte value)
        {
            return (value >= 0x40 && value <= 0x7e) || (value >= 0x80 && value <= 0xfe);
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
                    result.Total += ScoreAsciiByte(current);
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

            if (encoding == TextEncodingKind.Gbk)
                return EscapeGbkBytes(bytes);

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

        private static string EscapeGbkBytes(byte[] bytes)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 2);
            int position = 0;
            while (position < bytes.Length)
            {
                byte current = bytes[position];
                int count = 1;
                if (current >= 0x81 && current <= 0xfe)
                {
                    if (position + 1 < bytes.Length && IsGbkTrail(bytes[position + 1]))
                        count = 2;
                    else
                    {
                        AppendHexByte(result, current);
                        position++;
                        continue;
                    }
                }

                string decoded;
                try { decoded = BinaryUtil.GbkStrict.GetString(bytes, position, count); }
                catch (DecoderFallbackException)
                {
                    AppendHexByte(result, current);
                    position++;
                    continue;
                }

                byte[] encoded;
                try { encoded = BinaryUtil.GbkStrict.GetBytes(decoded); }
                catch (EncoderFallbackException)
                {
                    for (int i = 0; i < count; i++) AppendHexByte(result, bytes[position + i]);
                    position += count;
                    continue;
                }

                bool identical = encoded.Length == count;
                for (int i = 0; identical && i < count; i++) identical = encoded[i] == bytes[position + i];
                bool containsPrivateUse = decoded.Any(ch => ch >= '\ue000' && ch <= '\uf8ff');
                if (!identical || containsPrivateUse)
                {
                    // CP936／Windows-936 的使用者造字區沒有通用字形表；未知 PUA 不輸出成看似可編輯的字。
                    // 保留原 bytes，避免把遊戲字型專用碼位誤當一般 Unicode 字元。
                    for (int i = 0; i < count; i++) AppendHexByte(result, bytes[position + i]);
                }
                else
                {
                    foreach (char ch in decoded) AppendEscapedCharacter(result, ch);
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
                            if (encoding != TextEncodingKind.Cp932)
                                throw new InvalidDataException("\\g EUDC 跳脫只適用於 CP932；GBK／UTF-8／RAW 不可使用 CP932 私用造字位元組。");
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
                case TextEncodingKind.Gbk:
                    return BinaryUtil.GbkStrict.GetBytes(text);
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
                case "SHIFT_JIS":
                    return TextEncodingKind.Cp932;
                case "GBK":
                case "CP936":
                case "WINDOWS-936":
                case "GB2312":
                    return TextEncodingKind.Gbk;
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
