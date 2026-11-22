using System;
using System.Collections.Generic;
using System.IO;

namespace TmrHiroStudio.Core
{
    internal sealed class SrpInstruction
    {
        public short Type { get; set; }
        public short Flags { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// SRP 結構解析與 PAC 儲存層轉換。
    /// 文字編碼、SRP Data 顯示轉換及 VAR 解析統一交由 ScriptTextCodec，避免兩套解碼邏輯分歧。
    /// </summary>
    internal static class ScriptCodec
    {
        public static bool LooksLikePacSrp(byte[] data)
        {
            return data != null && data.Length >= 10 &&
                   BinaryUtil.ReadUInt16(data, 4) == 6 &&
                   BinaryUtil.ReadUInt32(data, 6) == 0x00140050;
        }

        /// <summary>
        /// PAC 封包儲存層對每筆 SRP Data 進行一次 nibble swap。
        /// 此轉換只處理 Data，不改變 SRP 指令標頭、長度、Type、Flags 或 footer。
        /// nibble swap 為自身反向，解包與回封使用同一函式。
        /// </summary>
        public static byte[] TransformPacScriptLayer(byte[] input)
        {
            if (!LooksLikePacSrp(input))
                throw new InvalidDataException("SRP 檔案缺少 Tmr-Hiro PAC 腳本識別標記。");

            // 先完整驗證結構，再開始修改 clone，避免壞檔產生半轉換結果。
            ReadSrpInstructions(input);

            byte[] result = (byte[])input.Clone();
            int position = 4;
            bool foundFooter = false;
            int instructionCount = 0;

            while (position < result.Length)
            {
                if (++instructionCount > 1000000 || position > result.Length - 6)
                    throw new InvalidDataException("SRP 指令標頭截斷或數量超出上限。");

                ushort instructionLength = BinaryUtil.ReadUInt16(result, position);
                short type = unchecked((short)BinaryUtil.ReadUInt16(result, position + 2));
                short flags = unchecked((short)BinaryUtil.ReadUInt16(result, position + 4));
                if (instructionLength < 4)
                    throw new InvalidDataException("SRP 指令長度小於 4。");

                int dataLength = instructionLength - 4;
                position += 6;
                BinaryUtil.EnsureRange(result.Length, position, dataLength, "SRP PAC Data");

                for (int i = 0; i < dataLength; i++)
                    result[position + i] = BinaryUtil.SwapNibbles(result[position + i]);

                position += dataLength;
                if (type == 16 && flags == 3 && dataLength == 0)
                {
                    foundFooter = true;
                    if (position != result.Length)
                        throw new InvalidDataException("SRP 結尾指令後仍有資料。");
                    break;
                }
            }

            if (!foundFooter)
                throw new InvalidDataException("SRP 缺少 Type 16／Flags 3 結尾指令。");
            return result;
        }

        public static List<SrpInstruction> ReadSrpInstructions(byte[] data)
        {
            if (data == null || data.Length < 10)
                throw new InvalidDataException("SRP 檔案太短。");

            ValidateSrpFooter(data);
            // 部分已知遊戲的 count 欄位可能不準確；以 footer 及檔案邊界作為實際解析依據。
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
                if (length < 4)
                    throw new InvalidDataException("SRP 指令長度小於 4。");

                int dataLength = length - 4;
                position += 6;
                BinaryUtil.EnsureRange(data.Length, position, dataLength, "SRP 指令 Data");

                byte[] payload = new byte[dataLength];
                if (dataLength > 0)
                    Buffer.BlockCopy(data, position, payload, 0, dataLength);

                instructions.Add(new SrpInstruction
                {
                    Type = type,
                    Flags = flags,
                    Data = payload
                });

                position += dataLength;
                if (type == 16 && flags == 3 && dataLength == 0)
                {
                    foundFooter = true;
                    if (position != data.Length)
                        throw new InvalidDataException("SRP 結尾指令後仍有多餘資料。");
                    break;
                }
            }

            if (!foundFooter || instructions.Count == 0)
                throw new InvalidDataException("SRP 缺少 Type 16／Flags 3 結尾指令。");

            return instructions;
        }

        private static void ValidateSrpFooter(byte[] data)
        {
            if (data.Length < 4 ||
                BinaryUtil.ReadUInt16(data, data.Length - 4) != 0x10 ||
                BinaryUtil.ReadUInt16(data, data.Length - 2) != 0x03)
                throw new InvalidDataException("SRP 結尾標記不正確（預期 10 00 03 00）。");
        }

        // 向後相容入口；實際轉換全部交由 ScriptTextCodec，避免舊、新編碼流程並存。
        public static string DecompileSrp(byte[] srpBytes)
        {
            return ScriptTextCodec.DecompileSrp(srpBytes);
        }

        public static byte[] CompileSrp(string text)
        {
            return ScriptTextCodec.CompileSrp(text);
        }

        public static string DecompileVar(byte[] data)
        {
            return ScriptTextCodec.DecompileVar(data);
        }

        public static byte[] CompileVar(string text)
        {
            return ScriptTextCodec.CompileVar(text);
        }
    }
}
