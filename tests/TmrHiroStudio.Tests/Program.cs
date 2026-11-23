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
                Run("使用者提供的 GBK 三組 SRP Data 正確解碼", TestUserGbkSamples);
                Run("opcode 0x00150050 訊息正確辨識與往返", TestMessageOpcode);
                Run("opcode 0x00140010 選項前綴與正文往返", TestChoiceOpcode);
                Run("Type 0 旁白依完整 opcode 辨識", TestNarrationOpcode);
                Run("未知 opcode 以原始 bytes 保留", TestUnknownOpcodeRaw);
                Run("無固定 footer 的有效 SRP 可解析", TestNoFooterRequired);
                Run("HeaderCount 不準確時仍保留原值與完整資料", TestMismatchedHeaderCount);
                Run("指令增刪後更新 HeaderCount", TestCountUpdatedWhenStructureChanges);
                Run("PAC 層 nibble swap 只處理 Data 並可逆", TestPacLayerReversible);
                Run("SRP 截斷資料拒絕解析", TestTruncatedSrpRejected);
                Run("GBK 無法解碼的 bytes 以 \\xHH 保留", TestInvalidGbkBytesEscaped);
                Run("GBK／CP936 VAR 往返", TestGbkVarRoundTrip);
                Run("CP932 VAR 往返", TestCp932VarRoundTrip);
                Run("UTF-8 VAR 往返", TestUtf8VarRoundTrip);
                Run("RAW VAR 往返", TestRawVarRoundTrip);
                Run("舊版 SRP TXT v3 相容", TestLegacySrpV3);
                Run("舊版 SRP TXT v3 的 Type 2／3 保持二進位規則", TestLegacySrpV3KeepsNonTextRaw);
                Run("舊版 VAR TXT v2 相容", TestLegacyVarV2);

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

        private static void TestUserGbkSamples()
        {
            byte[][] samples =
            {
                Hex("D4 DA C3 D4 C3 D4 BA FD BA FD D6 D0 A1 A2 B4 D3 CF C2 C3 E6 B4 AB C0 B4 D2 BB D6 D6 BD A5 BD A5 C0 A9 C9 A2 B5 C4 B8 D0 BE F5 A1 A3"),
                Hex("C4 DC CC FD B5 BD CE A2 C8 F5 B5 C4 BA F4 CE FC C9 F9 BA CD E0 DB E0 CD D2 BB C9 F9 CB AE B5 CE B5 AF BF AA B5 C4 C9 F9 D2 F4 A1 A3"),
                Hex("D7 F7 CE AA D0 D1 C0 B4 B5 C4 C9 F9 D2 F4 A1 A2 D5 E2 D6 D6 C9 F9 D2 F4 D3 D0 D2 BB D6 D6 B2 BB D0 AD B5 F7 B5 C4 B8 D0 BE F5 A1 A3")
            };
            byte[] original = BuildSrp(new[]
            {
                NewInstruction(0, 0, samples[0]),
                NewInstruction(0, 0, samples[1]),
                NewInstruction(0, 0, samples[2])
            }, 3);
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK");
            Assert(txt.Contains("在迷迷糊糊中、从下面传来一种渐渐扩散的感觉。"), "第一段 GBK 文字沒有完整解碼。");
            Assert(txt.Contains("能听到微弱的呼吸声和噗嗤一声水滴弹开的声音。"), "第二段 GBK 文字沒有完整解碼。");
            Assert(txt.Contains("作为醒来的声音、这种声音有一种不协调的感觉。"), "第三段 GBK 文字沒有完整解碼。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "使用者三組 GBK 樣本往返不一致");
            string autoTxt = ScriptTextCodec.DecompileSrp(original, "AUTO");
            Assert(autoTxt.Contains("Encoding: GBK"), "AUTO 未能辨識這三組 GBK 範例。");
        }

        private static void TestMessageOpcode()
        {
            byte[] body = BinaryUtil.GbkStrict.GetBytes("voice001,小明,这是一段测试台词。");
            byte[] original = BuildSrp(new[] { NewInstruction(80, 21, body) });
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK");
            Assert(txt.Contains("voice001,小明,这是一段测试台词。"), "0x00150050 訊息沒有被當成文字。");
            Assert(txt.Contains("Type: 80\nFlags: 21"), "訊息 opcode 的 Type／Flags 不正確。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "訊息指令往返不一致");
        }

        private static void TestChoiceOpcode()
        {
            byte[] bodyText = BinaryUtil.GbkStrict.GetBytes(" d是,YES_LABEL d否,NO_LABEL");
            byte[] body = new byte[] { 0x02, 0x03, 0x04 }.Concat(bodyText).ToArray();
            byte[] original = BuildSrp(new[] { NewInstruction(16, 20, body) });
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK");
            Assert(txt.Contains("\\x02\\x03\\x04 d是,YES_LABEL"), "選項 3-byte 前綴沒有保留。");
            Assert(txt.Contains("NO_LABEL"), "選項跳轉標籤沒有保留。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "選項指令往返不一致");
        }

        private static void TestNarrationOpcode()
        {
            byte[] text = BinaryUtil.GbkStrict.GetBytes("这是旁白文字。");
            byte[] original = BuildSrp(new[] { NewInstruction(0, 7, text) });
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK");
            Assert(txt.Contains("这是旁白文字。"), "低 16 位為 0 的旁白 opcode 未辨識。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "旁白指令往返不一致");
        }

        private static void TestUnknownOpcodeRaw()
        {
            byte[] unknown = { 0x00, 0x81, 0x40, 0xff, 0x10 };
            byte[] original = BuildSrp(new[] { NewInstruction(2, 3, unknown) });
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK");
            Assert(txt.Contains("\\x00\\x81\\x40\\xFF\\x10"), "未知指令 Data 沒有以原始 bytes 顯示。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "未知 opcode bytes 往返不一致");
        }

        private static void TestNoFooterRequired()
        {
            byte[] original = BuildSrp(new[]
            {
                NewInstruction(0, 0, BinaryUtil.ShiftJisStrict.GetBytes("footer not required"))
            });
            Assert(ScriptCodec.ReadSrpInstructions(original).Count == 1, "沒有 Type 16／Flags 3 footer 的 SRP 不應被拒絕。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(ScriptTextCodec.DecompileSrp(original, "CP932")),
                "無 footer SRP 往返不一致");
        }

        private static void TestMismatchedHeaderCount()
        {
            byte[] original = BuildSrp(new[]
            {
                NewInstruction(0, 0, BinaryUtil.GbkStrict.GetBytes("第一筆")),
                NewInstruction(0, 0, BinaryUtil.GbkStrict.GetBytes("第二筆"))
            }, 99);
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK");
            Assert(txt.Contains("HeaderCount: 99"), "匯出沒有保存不準確的原始 HeaderCount。");
            Assert(txt.Contains("ParsedCount: 2"), "匯出實際指令數不正確。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "原始 HeaderCount 不準確時往返不一致");
        }

        private static void TestCountUpdatedWhenStructureChanges()
        {
            byte[] original = BuildSrp(new[]
            {
                NewInstruction(0, 0, BinaryUtil.GbkStrict.GetBytes("第一筆")),
                NewInstruction(0, 0, BinaryUtil.GbkStrict.GetBytes("第二筆"))
            }, 2);
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK");
            int marker = txt.IndexOf("Type: 0\nFlags: 0\nData: 第二筆", StringComparison.Ordinal);
            Assert(marker >= 0, "找不到預期的第二筆指令。");
            int end = txt.IndexOf("\n\n", marker, StringComparison.Ordinal);
            if (end < 0) end = txt.Length;
            else end += 2;
            txt = txt.Remove(marker, end - marker);
            byte[] rebuilt = ScriptTextCodec.CompileSrp(txt);
            Assert(BinaryUtil.ReadUInt32(rebuilt, 0) == 1, "刪除指令後 HeaderCount 沒有更新。");
            Assert(ScriptCodec.ReadSrpInstructions(rebuilt).Count == 1, "刪除指令後 SRP 實際指令數不正確。");
        }

        private static void TestPacLayerReversible()
        {
            byte[] original = BuildSrp(new[]
            {
                NewInstruction(80, 20, new byte[] { 0x12, 0x34 }), // Length=6，PAC script 標記 0x00140050
                NewInstruction(0, 0, BinaryUtil.GbkStrict.GetBytes("PAC storage layer")),
                NewInstruction(16, 3, Array.Empty<byte>())
            });
            Assert(ScriptCodec.HasPacSrpSignature(original), "合成 PAC SRP 缺少上游標記。");
            byte[] stored = ScriptCodec.TransformPacScriptLayer(original);
            Assert(!stored.SequenceEqual(original), "PAC 層 nibble swap 沒有改變 Data。");
            Assert(original.Take(10).SequenceEqual(stored.Take(10)), "PAC 層轉換不應修改 count、length、Type 或 Flags。");
            Assert(stored[10] == 0x21 && stored[11] == 0x43, "首筆識別指令的 Data 也必須依規則交換 nibble。");
            byte[] restored = ScriptCodec.TransformPacScriptLayer(stored);
            AssertBytesEqual(original, restored, "PAC 層 nibble swap 兩次後未還原。");
        }

        private static void TestTruncatedSrpRejected()
        {
            byte[] valid = BuildSrp(new[] { NewInstruction(0, 0, new byte[] { 0x41, 0x42 }) });
            byte[] truncated = valid.Take(valid.Length - 1).ToArray();
            bool rejected = false;
            try { ScriptCodec.ReadSrpInstructions(truncated); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "截斷 SRP 不應通過結構驗證。");
        }

        private static void TestInvalidGbkBytesEscaped()
        {
            byte[] original = BuildSrp(new[] { NewInstruction(0, 0, new byte[] { 0xff, 0x41, 0x42 }) });
            string txt = ScriptTextCodec.DecompileSrp(original, "GBK");
            Assert(txt.Contains("\\xFFAB"), "無效 GBK 位元組未以 \\xHH 保存。");
            AssertBytesEqual(original, ScriptTextCodec.CompileSrp(txt), "無效 GBK bytes 往返不一致");
        }

        private static void TestGbkVarRoundTrip()
        {
            byte[] original = BuildVar(new[]
            {
                BinaryUtil.GbkStrict.GetBytes("简体中文变量"),
                BinaryUtil.GbkStrict.GetBytes("第二行"),
                Encoding.ASCII.GetBytes("[END]")
            });
            string txt = ScriptTextCodec.DecompileVar(original, "GBK");
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
            AssertBytesEqual(original, ScriptTextCodec.CompileVar(ScriptTextCodec.DecompileVar(original, "CP932")),
                "CP932 VAR 往返不一致");
        }

        private static void TestUtf8VarRoundTrip()
        {
            byte[] original = BuildVar(new[]
            {
                BinaryUtil.Utf8Strict.GetBytes("UTF-8 VAR 測試"),
                Encoding.ASCII.GetBytes("[END]")
            });
            AssertBytesEqual(original, ScriptTextCodec.CompileVar(ScriptTextCodec.DecompileVar(original, "UTF8")),
                "UTF-8 VAR 往返不一致");
        }

        private static void TestRawVarRoundTrip()
        {
            byte[] original = BuildVar(new[]
            {
                new byte[] { 0xff, 0x00, 0x81 },
                Encoding.ASCII.GetBytes("[END]")
            });
            AssertBytesEqual(original, ScriptTextCodec.CompileVar(ScriptTextCodec.DecompileVar(original, "RAW")),
                "RAW VAR 往返不一致");
        }

        private static void TestLegacySrpV3()
        {
            byte[] source = BuildSrp(new[]
            {
                NewInstruction(0, 0, BinaryUtil.GbkStrict.GetBytes("舊格式旁白相容")),
                NewInstruction(1, 2, BinaryUtil.GbkStrict.GetBytes("舊格式選項相容")),
                NewInstruction(16, 3, Array.Empty<byte>())
            });
            string legacy =
                "TMRHIRO-SRP-TEXT/3\n" +
                "Encoding: GBK\n" +
                "Transform: NONE\n" +
                "HeaderCount: 3\n" +
                "ParsedCount: 3\n\n" +
                "Type: 0\nFlags: 0\nData: 舊格式旁白相容\n\n" +
                "Type: 1\nFlags: 2\nData: 舊格式選項相容\n\n" +
                "Type: 16\nFlags: 3\nData: \n";
            Assert(legacy.Contains("舊格式選項相容"), "舊版 TXT v3 測試資料未包含 Type 1 文字。");
            AssertBytesEqual(source, ScriptTextCodec.CompileSrp(legacy), "舊版 SRP TXT v3 不相容");
        }

        private static void TestLegacySrpV3KeepsNonTextRaw()
        {
            string legacy =
                "TMRHIRO-SRP-TEXT/3\n" +
                "Encoding: GBK\n" +
                "Transform: NONE\n" +
                "HeaderCount: 1\n" +
                "ParsedCount: 1\n\n" +
                "Type: 2\n" +
                "Flags: 3\n" +
                "Data: 這段舊格式二進位欄位不可當成一般文字\n";

            bool rejected = false;
            try { ScriptTextCodec.CompileSrp(legacy); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "舊版 v3 的 Type 2 Data 應要求 \\xHH，不能被當作文字重新編碼。");
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
            AssertBytesEqual(original, ScriptTextCodec.CompileVar(v2), "舊版 VAR TXT v2 不相容");
        }

        private static SrpInstruction NewInstruction(short type, short flags, byte[] data)
        {
            return new SrpInstruction { Type = type, Flags = flags, Data = data };
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

        private static byte[] Hex(string value)
        {
            string[] parts = value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(part => Convert.ToByte(part, 16)).ToArray();
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
