using System;
using System.IO;
using System.Linq;
using System.Text;
using TmrHiroStudio.Core;

namespace TmrHiroStudio.Tests
{
    internal static class Program
    {
        private static int _passed;

        private static int Main()
        {
            try
            {
                Run("GBK／CP936 SRP 手動編碼往返", TestGbkSrpRoundTrip);
                Run("GBK／CP936 SRP 與 NIBBLE_SWAP 獨立往返", TestGbkSrpWithNibbleSwap);
                Run("CP932 SRP 手動編碼往返", TestCp932SrpRoundTrip);
                Run("UTF-8 SRP 手動編碼往返", TestUtf8SrpRoundTrip);
                Run("RAW SRP 保留未知位元組", TestRawSrpRoundTrip);
                Run("無效 GBK 位元組以 \\xHH 保留", TestInvalidGbkBytesAreEscaped);
                Run("GBK 私用造字位元組保留原始 bytes", TestGbkPrivateUseBytesAreEscaped);
                Run("SRP 原始 HeaderCount 在未改指令數時保留", TestSrpHeaderCountPreserved);
                Run("SRP 新增／刪除指令後更新 HeaderCount", TestSrpHeaderCountUpdatedOnStructureChange);
                Run("舊版 SRP v2 TXT 仍可回封", TestLegacySrpV2);
                Run("GBK／CP936 VAR 手動編碼往返", TestGbkVarRoundTrip);
                Run("CP932 VAR 手動編碼往返", TestCp932VarRoundTrip);
                Run("UTF-8 VAR 手動編碼往返", TestUtf8VarRoundTrip);
                Run("RAW VAR 保留未知位元組", TestRawVarRoundTrip);
                Run("舊版 VAR v2 TXT 仍可回封", TestLegacyVarV2);
                Run("PAC SRP 必須通過完整結構驗證", TestPacSrpRequiresFullStructure);
                Run("PAC 層 nibble swap 可逆", TestPacLayerIsReversible);

                Console.WriteLine();
                Console.WriteLine("全部測試通過：" + _passed);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("測試失敗：" + ex);
                return 1;
            }
        }

        private static void Run(string name, Action action)
        {
            action();
            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static void TestGbkSrpRoundTrip()
        {
            string source = "「简体中文测试：游戏台词（GBK）」";
            byte[] original = BuildSrp(new[]
            {
                new SrpInstruction { Type = 0, Flags = 0, Data = BinaryUtil.GbkStrict.GetBytes(source) },
                new SrpInstruction { Type = 1, Flags = 2, Data = BinaryUtil.GbkStrict.GetBytes("是／否") },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            });

            string txt = ScriptTextCodec.DecompileSrp(original, "GBK", "NONE");
            Assert(txt.StartsWith("TMRHIRO-SRP-TEXT/3\n", StringComparison.Ordinal), "未輸出 SRP TXT v3 標頭。");
            Assert(txt.Contains("Encoding: GBK"), "沒有記錄 GBK 編碼。");
            Assert(txt.Contains("Transform: NONE"), "文字編碼選擇不應自動改變 Transform。");
            Assert(txt.Contains(source), "GBK 內容沒有正確解碼。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "GBK SRP 往返不一致");
        }

        private static void TestGbkSrpWithNibbleSwap()
        {
            byte[] plain = BuildSrp(new[]
            {
                new SrpInstruction { Type = 0, Flags = 0, Data = BinaryUtil.GbkStrict.GetBytes("「交换层测试」") },
                // 二進位 Type 2 同樣屬於 SRP Data Transform 範圍，必須原樣反向還原。
                new SrpInstruction { Type = 2, Flags = 3, Data = new byte[] { 0x12, 0x34, 0x56 } },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            });
            byte[] stored = SwapAllSrpData(plain);

            string txt = ScriptTextCodec.DecompileSrp(stored, "GBK", "NIBBLE_SWAP");
            Assert(txt.Contains("Encoding: GBK"), "選擇 nibble swap 不得改變 GBK 編碼。");
            Assert(txt.Contains("Transform: NIBBLE_SWAP"), "未將 nibble swap 獨立記錄。");
            Assert(txt.Contains("交换层测试"), "先交換再以 GBK 解碼失敗。");
            Assert(txt.Contains("\\x12\\x34\\x56"), "Type 2 二進位 Data 未以轉換後的原始 bytes 顯示。");
            AssertBytesEqual(stored, ScriptTextCodec.CompileSrp(txt), "GBK + NIBBLE_SWAP 往返不一致");
        }

        private static void TestCp932SrpRoundTrip()
        {
            byte[] original = BuildSrp(new[]
            {
                new SrpInstruction { Type = 0, Flags = 0, Data = BinaryUtil.ShiftJisStrict.GetBytes("「日本語の台詞です。」") },
                new SrpInstruction { Type = 1, Flags = 0, Data = BinaryUtil.ShiftJisStrict.GetBytes("はい／いいえ") },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            });
            string txt = ScriptTextCodec.DecompileSrp(original, "CP932", "NONE");
            Assert(txt.Contains("日本語の台詞"), "CP932 日文未正確顯示。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "CP932 SRP 往返不一致");
        }

        private static void TestUtf8SrpRoundTrip()
        {
            byte[] original = BuildSrp(new[]
            {
                new SrpInstruction { Type = 0, Flags = 0, Data = BinaryUtil.Utf8Strict.GetBytes("「UTF-8 中文測試」") },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            });
            string txt = ScriptTextCodec.DecompileSrp(original, "UTF8", "NONE");
            Assert(txt.Contains("Encoding: UTF8"), "未記錄 UTF-8 編碼。");
            Assert(txt.Contains("UTF-8 中文測試"), "UTF-8 文字未正確顯示。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "UTF-8 SRP 往返不一致");
        }

        private static void TestRawSrpRoundTrip()
        {
            byte[] original = BuildSrp(new[]
            {
                new SrpInstruction { Type = 0, Flags = 0, Data = new byte[] { 0xff, 0xfe, 0x84, 0x18, 0x00 } },
                new SrpInstruction { Type = 2, Flags = 3, Data = new byte[] { 0x00, 0x81, 0x40, 0xff } },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            });
            string txt = ScriptTextCodec.DecompileSrp(original, "RAW", "NONE");
            Assert(txt.Contains("Encoding: RAW"), "RAW 模式沒有記錄於標頭。");
            Assert(txt.Contains("\\xFF\\xFE\\x84\\x18\\x00"), "RAW 文字 Data 沒有轉成逐 byte 跳脫。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "RAW SRP 往返不一致");
        }

        private static void TestInvalidGbkBytesAreEscaped()
        {
            byte[] original = BuildSrp(new[]
            {
                new SrpInstruction { Type = 0, Flags = 0, Data = new byte[] { 0xff, 0x41, 0x42 } },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            });
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK", "NONE");
            Assert(txt.Contains("\\xFFAB"), "無效 GBK 位元組未以 \\xHH 保存。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "無效 GBK bytes 往返不一致");
        }

        private static void TestGbkPrivateUseBytesAreEscaped()
        {
            byte[] original = BuildSrp(new[]
            {
                new SrpInstruction { Type = 0, Flags = 0, Data = new byte[] { 0xf8, 0xa1 } },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            });
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK", "NONE");
            Assert(txt.Contains("\\xF8\\xA1"), "GBK 私用造字未以原始 bytes 保留。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "GBK 私用造字 bytes 往返不一致");
        }

        private static void TestSrpHeaderCountPreserved()
        {
            byte[] original = BuildSrp(new[]
            {
                new SrpInstruction { Type = 0, Flags = 0, Data = BinaryUtil.GbkStrict.GetBytes("保留 count") },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            }, 0x12345678);
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK", "NONE");
            Assert(txt.Contains("HeaderCount: 305419896"), "TXT 未記錄來源 HeaderCount。");
            Assert(txt.Contains("ParsedCount: 2"), "TXT 未記錄實際指令數。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "未編輯指令的 HeaderCount 未保持原值");
        }

        private static void TestSrpHeaderCountUpdatedOnStructureChange()
        {
            byte[] original = BuildSrp(new[]
            {
                new SrpInstruction { Type = 0, Flags = 0, Data = BinaryUtil.GbkStrict.GetBytes("第一筆") },
                new SrpInstruction { Type = 1, Flags = 0, Data = BinaryUtil.GbkStrict.GetBytes("第二筆") },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            }, 3);
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK", "NONE");
            string second = ExtractInstructionBlock(txt, "Type: 1");
            Assert(second.Length > 0, "測試 TXT 找不到第二筆指令。");
            txt = txt.Replace(second, string.Empty);
            byte[] rebuilt = ScriptTextCodec.CompileSrp(txt);
            Assert(BinaryUtil.ReadUInt32(rebuilt, 0) == 2, "刪除一筆指令後 HeaderCount 沒有更新。");
            Assert(ScriptCodec.ReadSrpInstructions(rebuilt).Count == 2, "結構修改後 SRP 指令數不正確。");
        }

        private static void TestLegacySrpV2()
        {
            byte[] original = BuildSrp(new[]
            {
                new SrpInstruction { Type = 0, Flags = 0, Data = BinaryUtil.ShiftJisStrict.GetBytes("舊版標頭相容") },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            });
            string v3 = ScriptTextCodec.DecompileSrp(original, "CP932", "NONE");
            string v2 = v3.Replace("TMRHIRO-SRP-TEXT/3", "TMRHIRO-SRP-TEXT/2")
                .Replace("HeaderCount: 2\n", string.Empty)
                .Replace("ParsedCount: 2\n", string.Empty);
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(v2), "舊版 SRP v2 TXT 不相容");
        }

        private static void TestGbkVarRoundTrip()
        {
            byte[] original = BuildVar(new[]
            {
                BinaryUtil.GbkStrict.GetBytes("简体中文变量"),
                BinaryUtil.GbkStrict.GetBytes("第二行") ,
                Encoding.ASCII.GetBytes("[END]")
            });
            string txt = ScriptTextCodec.DecompileVar(original, "GBK");
            Assert(txt.StartsWith("TMRHIRO-VAR-TEXT/3\n", StringComparison.Ordinal), "VAR 未使用 v3 標頭。");
            Assert(txt.Contains("Encoding: GBK"), "VAR 未記錄 GBK。");
            Assert(txt.Contains("简体中文变量"), "GBK VAR 文字沒有顯示。");
            AssertBytesEqual(original, ScriptTextCodec.CompileVar(txt), "GBK VAR 往返不一致");
        }

        private static void TestCp932VarRoundTrip()
        {
            byte[] original = BuildVar(new[]
            {
                BinaryUtil.ShiftJisStrict.GetBytes("日本語 VAR の確認"),
                Encoding.ASCII.GetBytes("[END]")
            });
            string txt = ScriptTextCodec.DecompileVar(original, "CP932");
            Assert(txt.Contains("日本語 VAR"), "CP932 VAR 文字沒有顯示。");
            AssertBytesEqual(original, ScriptTextCodec.CompileVar(txt), "CP932 VAR 往返不一致");
        }

        private static void TestUtf8VarRoundTrip()
        {
            byte[] original = BuildVar(new[]
            {
                BinaryUtil.Utf8Strict.GetBytes("UTF-8 VAR 測試"),
                Encoding.ASCII.GetBytes("[END]")
            });
            string txt = ScriptTextCodec.DecompileVar(original, "UTF8");
            Assert(txt.Contains("Encoding: UTF8"), "VAR 沒有記錄 UTF-8。");
            AssertBytesEqual(original, ScriptTextCodec.CompileVar(txt), "UTF-8 VAR 往返不一致");
        }

        private static void TestRawVarRoundTrip()
        {
            byte[] original = BuildVar(new[]
            {
                new byte[] { 0xff, 0x00, 0x81 },
                Encoding.ASCII.GetBytes("[END]")
            });
            string txt = ScriptTextCodec.DecompileVar(original, "RAW");
            Assert(txt.Contains("Encoding: RAW"), "VAR RAW 標頭缺少 Encoding。");
            AssertBytesEqual(original, ScriptTextCodec.CompileVar(txt), "RAW VAR 往返不一致");
        }

        private static void TestLegacyVarV2()
        {
            byte[] original = BuildVar(new[]
            {
                BinaryUtil.ShiftJisStrict.GetBytes("舊版 VAR 標頭"),
                Encoding.ASCII.GetBytes("[END]")
            });
            string v3 = ScriptTextCodec.DecompileVar(original, "CP932");
            string v2 = v3.Replace("TMRHIRO-VAR-TEXT/3", "TMRHIRO-VAR-TEXT/2");
            AssertBytesEqual(original, ScriptTextCodec.CompileVar(v2), "舊版 VAR v2 TXT 不相容");
        }

        private static void TestPacSrpRequiresFullStructure()
        {
            byte[] valid = BuildSrp(new[]
            {
                new SrpInstruction { Type = 80, Flags = 20, Data = new byte[] { 0x12, 0x34 } },
                new SrpInstruction { Type = 0, Flags = 0, Data = BinaryUtil.ShiftJisStrict.GetBytes("PAC 腳本") },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            });
            Assert(ScriptCodec.HasPacSrpSignature(valid), "合成 SRP 缺少預期 PAC signature。");
            Assert(ScriptCodec.LooksLikePacSrp(valid), "合法完整 SRP 未通過結構驗證。");
            byte[] truncated = valid.Take(12).ToArray();
            Assert(ScriptCodec.HasPacSrpSignature(truncated), "測試用截斷資料應保留開頭 signature。");
            Assert(!ScriptCodec.LooksLikePacSrp(truncated), "截斷 SRP 不應通過完整結構驗證。");
        }

        private static void TestPacLayerIsReversible()
        {
            byte[] original = BuildSrp(new[]
            {
                new SrpInstruction { Type = 80, Flags = 20, Data = new byte[] { 0x12, 0x34 } },
                new SrpInstruction { Type = 0, Flags = 0, Data = BinaryUtil.GbkStrict.GetBytes("PAC 轉換層") },
                new SrpInstruction { Type = 16, Flags = 3, Data = Array.Empty<byte>() }
            });
            byte[] transformed = ScriptCodec.TransformPacScriptLayer(original);
            byte[] restored = ScriptCodec.TransformPacScriptLayer(transformed);
            AssertBytesEqual(original, restored, "PAC SRP nibble swap 兩次後未還原");
        }

        private static byte[] BuildSrp(SrpInstruction[] instructions, uint? headerCount = null)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(headerCount ?? (uint)instructions.Length);
                foreach (SrpInstruction instruction in instructions)
                {
                    writer.Write((ushort)(4 + instruction.Data.Length));
                    writer.Write(instruction.Type);
                    writer.Write(instruction.Flags);
                    writer.Write(instruction.Data);
                }
                return stream.ToArray();
            }
        }

        private static byte[] BuildVar(byte[][] values)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                foreach (byte[] value in values)
                {
                    writer.Write((ushort)value.Length);
                    writer.Write(value);
                }
                return stream.ToArray();
            }
        }

        private static byte[] SwapAllSrpData(byte[] source)
        {
            byte[] result = (byte[])source.Clone();
            int position = 4;
            while (position < result.Length)
            {
                ushort instructionLength = BinaryUtil.ReadUInt16(result, position);
                int dataLength = instructionLength - 4;
                position += 6;
                for (int i = 0; i < dataLength; i++)
                    result[position + i] = BinaryUtil.SwapNibbles(result[position + i]);
                position += dataLength;
            }
            return result;
        }

        private static string ExtractInstructionBlock(string text, string marker)
        {
            int start = text.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            int end = text.IndexOf("\n\n", start, StringComparison.Ordinal);
            if (end < 0) return text.Substring(start);
            return text.Substring(start, end - start + 2);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void AssertBytesEqual(byte[] expected, byte[] actual, string message)
        {
            if (expected.Length != actual.Length)
                throw new InvalidOperationException(message + "（長度不同：" + expected.Length + " / " + actual.Length + "）");
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i])
                    throw new InvalidOperationException(message + "（offset 0x" + i.ToString("X") +
                        "：預期 0x" + expected[i].ToString("X2") + "，實際 0x" + actual[i].ToString("X2") + "）");
            }
        }
    }
}
