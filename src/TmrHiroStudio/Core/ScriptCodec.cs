using System;
using System.Collections.Generic;
using System.IO;

namespace TmrHiroStudio.Core
{
    /// <summary>SRP 單筆指令；Opcode = Flags:Type（little-endian UInt32）。</summary>
    internal sealed class SrpInstruction
    {
        public short Type { get; set; }
        public short Flags { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public int RecordOffset { get; set; }
        public int DataOffset { get; set; }
        public uint Opcode { get { return ((uint)(ushort)Flags << 16) | (ushort)Type; } }
    }

    /// <summary>
    /// Tmr-Hiro SRP 結構與 PAC 儲存層轉換。
    /// 每筆指令依 UInt16 length 解析至 EOF；不把特定 footer 當成所有版本的必要條件。
    /// PAC 層 nibble swap 只作用在各筆 Data，且與文字編碼完全分離。
    /// </summary>
    internal static class ScriptCodec
    {
        private const int HeaderSize = 4;
        private const int RecordHeaderSize = 6;
        private const int MaximumInstructionCount = 1000000;

        public static bool HasPacSrpSignature(byte[] data)
        {
            return data != null && data.Length >= 10 &&
                   BinaryUtil.ReadUInt16(data, 4) == 6 &&
                   BinaryUtil.ReadUInt32(data, 6) == 0x00140050;
        }

        public static bool LooksLikePacSrp(byte[] data)
        {
            if (!HasPacSrpSignature(data)) return false;
            try
            {
                ReadSrpInstructions(data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>只轉換每筆 Data；count、length、Type、Flags 均保持不變。</summary>
        public static byte[] TransformPacScriptLayer(byte[] input)
        {
            if (!LooksLikePacSrp(input))
                throw new InvalidDataException("SRP 資源不符合 Tmr-Hiro PAC script 標記或指令結構。");

            List<SrpInstruction> instructions = ReadSrpInstructions(input);
            byte[] result = (byte[])input.Clone();
            foreach (SrpInstruction instruction in instructions)
            {
                for (int i = 0; i < instruction.Data.Length; i++)
                {
                    int offset = instruction.DataOffset + i;
                    result[offset] = BinaryUtil.SwapNibbles(result[offset]);
                }
            }
            return result;
        }

        /// <summary>
        /// 讀取 UInt32 count 欄位後依 length 解析至 EOF。
        /// count 保留作為 metadata，不作截斷條件：已知舊遊戲樣本存在 count 不準確的情況。
        /// 每筆 length 的定義為 4-byte Opcode + Data，不包含前方 2-byte length。
        /// </summary>
        public static List<SrpInstruction> ReadSrpInstructions(byte[] data)
        {
            if (data == null || data.Length < HeaderSize + RecordHeaderSize)
                throw new InvalidDataException("SRP 檔案太短，沒有完整指令。");
            _ = BinaryUtil.ReadUInt32(data, 0);

            List<SrpInstruction> instructions = new List<SrpInstruction>();
            int position = HeaderSize;
            while (position < data.Length)
            {
                if (instructions.Count >= MaximumInstructionCount)
                    throw new InvalidDataException("SRP 指令數量超出安全上限。");
                if (position > data.Length - RecordHeaderSize)
                    throw new InvalidDataException("SRP 最後一筆指令標頭遭截斷。");

                ushort length = BinaryUtil.ReadUInt16(data, position);
                if (length < 4)
                    throw new InvalidDataException("SRP 指令 length 小於 4。");
                int recordSize = checked(2 + (int)length);
                BinaryUtil.EnsureRange(data.Length, position, recordSize, "SRP record");

                short type = unchecked((short)BinaryUtil.ReadUInt16(data, position + 2));
                short flags = unchecked((short)BinaryUtil.ReadUInt16(data, position + 4));
                int dataLength = length - 4;
                int dataOffset = position + RecordHeaderSize;
                byte[] payload = new byte[dataLength];
                if (dataLength > 0) Buffer.BlockCopy(data, dataOffset, payload, 0, dataLength);

                instructions.Add(new SrpInstruction
                {
                    Type = type,
                    Flags = flags,
                    Data = payload,
                    RecordOffset = position,
                    DataOffset = dataOffset
                });
                position += recordSize;
            }

            if (position != data.Length || instructions.Count == 0)
                throw new InvalidDataException("SRP 指令結構未完整覆蓋檔案。");
            return instructions;
        }

        public static string DecompileSrp(byte[] srpBytes) { return ScriptTextCodec.DecompileSrp(srpBytes); }
        public static byte[] CompileSrp(string text) { return ScriptTextCodec.CompileSrp(text); }
        public static string DecompileVar(byte[] data) { return ScriptTextCodec.DecompileVar(data); }
        public static byte[] CompileVar(string text) { return ScriptTextCodec.CompileVar(text); }
    }
}
