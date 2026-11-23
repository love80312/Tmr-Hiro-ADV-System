using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using TmrHiroStudio.Core;

namespace TmrHiroStudio
{
    /// <summary>
    /// Corrected TXT -> SRP/VAR batch builders.
    /// Uses ScriptTextFileCodec so TXT v4 is read as UTF-8, while the Encoding
    /// field inside the TXT controls only the SRP payload encoding.
    /// </summary>
    public partial class MainWindow
    {
        private async void SrpBuildFixed_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP 回封", () => PrepareScriptBuildFixed(srp: true));
        }

        private async void VarBuildFixed_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("VAR 回封", () => PrepareScriptBuildFixed(srp: false));
        }

        private async void BothBuildFixed_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP + VAR 批次回封", PrepareCombinedScriptBuildFixed);
        }

        private Action PrepareScriptBuildFixed(bool srp)
        {
            string input = RequirePath(ScriptBuildInput, "腳本文字檔／資料夾");
            string output = RequirePath(ScriptBuildOutput, "腳本輸出資料夾");
            PathHistory.RememberInputPath(input);
            PathHistory.RememberOutputDirectory(output);

            return () =>
            {
                List<string> files = GetScriptTextFilesForBuildFixed(input, srp);
                if (files.Count == 0)
                    throw new InvalidDataException("找不到可辨識的 " + (srp ? "SRP" : "VAR") +
                        " 文字檔。請確認副檔名、格式標頭或舊版 Type／Flags／Data 結構。");

                bool folderMode = Directory.Exists(input);
                List<Tuple<string, byte[]>> pending = new List<Tuple<string, byte[]>>();
                HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string resourceExtension = srp ? ".srp" : ".var";

                // Parse and compile every input before creating any output.
                foreach (string file in files)
                {
                    string relative = folderMode
                        ? BinaryUtil.GetRelativePathCompat(input, file)
                        : Path.GetFileName(file);
                    string outputRelative = GetCompiledResourcePath(relative, resourceExtension);
                    ValidateRelativeOutputPath(outputRelative);
                    if (!destinations.Add(outputRelative))
                        throw new InvalidDataException("不同 TXT 來源會產生相同二進位輸出路徑：" + outputRelative);

                    string sourceText = ScriptTextFileCodec.ReadText(file);
                    byte[] compiled = srp
                        ? ScriptTextCodec.CompileSrp(sourceText)
                        : ScriptTextCodec.CompileVar(sourceText);
                    pending.Add(Tuple.Create(outputRelative, compiled));
                }

                CommitBatchNoOverwrite(output, pending);
                foreach (Tuple<string, byte[]> item in pending)
                    LogLine("TXT → " + (srp ? "SRP：" : "VAR：") + item.Item1);
                LogLine((srp ? "SRP" : "VAR") + " 回封完成：" + pending.Count +
                    " 個檔案。既有輸出不會被覆蓋。");
            };
        }

        private Action PrepareCombinedScriptBuildFixed()
        {
            string input = RequirePath(ScriptBuildInput, "SRP／VAR 文字檔資料夾");
            string output = RequirePath(ScriptBuildOutput, "腳本輸出資料夾");
            if (!Directory.Exists(input))
                throw new InvalidOperationException("SRP + VAR 一起回封時，請選擇同時包含 .srp.txt 與 .var.txt 的資料夾。");

            PathHistory.RememberInputPath(input);
            PathHistory.RememberOutputDirectory(output);

            return () =>
            {
                List<string> srpFiles = GetScriptTextFilesForBuildFixed(input, true);
                List<string> varFiles = GetScriptTextFilesForBuildFixed(input, false);

                if (srpFiles.Count == 0 || varFiles.Count == 0)
                    throw new InvalidDataException(
                        "SRP + VAR 一起回封需要同時找到 SRP 與 VAR TXT。" +
                        "目前找到 SRP " + srpFiles.Count + " 個、VAR " + varFiles.Count + " 個。");

                List<Tuple<string, bool>> sources = new List<Tuple<string, bool>>();
                sources.AddRange(srpFiles.Select(file => Tuple.Create(file, true)));
                sources.AddRange(varFiles.Select(file => Tuple.Create(file, false)));

                List<Tuple<string, byte[]>> pending = new List<Tuple<string, byte[]>>();
                HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Compile all SRP and VAR TXT first. A bad file prevents the whole batch
                // from being committed, so no partial SRP-only output is left behind.
                foreach (Tuple<string, bool> source in sources.OrderBy(
                    item => item.Item1, StringComparer.OrdinalIgnoreCase))
                {
                    string relative = BinaryUtil.GetRelativePathCompat(input, source.Item1);
                    string outputRelative = GetCompiledResourcePath(relative, source.Item2 ? ".srp" : ".var");
                    ValidateRelativeOutputPath(outputRelative);
                    if (!destinations.Add(outputRelative))
                        throw new InvalidDataException("不同 TXT 來源會產生相同二進位輸出路徑：" + outputRelative);

                    string sourceText = ScriptTextFileCodec.ReadText(source.Item1);
                    byte[] compiled = source.Item2
                        ? ScriptTextCodec.CompileSrp(sourceText)
                        : ScriptTextCodec.CompileVar(sourceText);
                    pending.Add(Tuple.Create(outputRelative, compiled));
                }

                CommitBatchNoOverwrite(output, pending);
                foreach (Tuple<string, byte[]> item in pending)
                    LogLine("TXT → " + (item.Item1.EndsWith(".srp", StringComparison.OrdinalIgnoreCase) ? "SRP：" : "VAR：") + item.Item1);
                LogLine("SRP + VAR 一起回封完成：SRP " + srpFiles.Count + " 個、VAR " + varFiles.Count +
                    " 個，共 " + pending.Count + " 個檔案。既有輸出不會被覆蓋。");
            };
        }

        private static List<string> GetScriptTextFilesForBuildFixed(string path, bool srp)
        {
            // Keep all existing legacy/header/suffix recognition rules.
            List<string> files = GetScriptTextFilesEnhanced(path, srp);
            if (!Directory.Exists(path))
                return files;

            // The previous discovery list omitted SRP TXT v4 when users renamed
            // the usual *.srp.txt suffix. Recognize its explicit ASCII format header.
            string expected = srp ? "TMRHIRO-SRP-TEXT/4" : "TMRHIRO-VAR-TEXT/4";
            HashSet<string> seen = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.EnumerateFiles(path, "*.txt", SearchOption.AllDirectories))
            {
                if (!seen.Contains(file) && HasAsciiFirstLine(file, expected))
                {
                    files.Add(file);
                    seen.Add(file);
                }
            }

            return files.OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool HasAsciiFirstLine(string path, string expected)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    // Accept an optional UTF-8 BOM before the ASCII format header.
                    if (stream.Length >= 3)
                    {
                        int first = stream.ReadByte();
                        int second = stream.ReadByte();
                        int third = stream.ReadByte();
                        if (first != 0xef || second != 0xbb || third != 0xbf)
                            stream.Position = 0;
                    }

                    List<byte> line = new List<byte>();
                    while (line.Count < 128)
                    {
                        int value = stream.ReadByte();
                        if (value < 0 || value == 0x0a || value == 0x0d)
                            break;
                        if (value < 0x20 || value > 0x7e)
                            return false;
                        line.Add((byte)value);
                    }
                    return string.Equals(Encoding.ASCII.GetString(line.ToArray()).Trim(), expected,
                        StringComparison.Ordinal);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
