using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TmrHiroStudio.Core;

namespace TmrHiroStudio
{
    /// <summary>
    /// SRP／VAR 操作直接由 XAML 綁定，不再於 OnContentRendered 搜尋／替換舊按鈕事件。
    /// 批次作業先解析所有來源，再寫入同層 staging 目錄，預設不覆蓋既有輸出。
    /// </summary>
    public partial class MainWindow
    {
        private async void SrpExportMapped_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP 匯出", () => PrepareScriptExport(srpOnly: true, both: false));
        }

        private async void VarExportMapped_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("VAR 匯出", () => PrepareScriptExport(srpOnly: false, both: false));
        }

        private async void BothExport_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP + VAR 批次匯出", () => PrepareScriptExport(srpOnly: false, both: true));
        }

        private async void SrpBuildMapped_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP 回封", () => PrepareScriptBuild(srp: true));
        }

        private async void VarBuildMapped_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("VAR 回封", () => PrepareScriptBuild(srp: false));
        }

        private Action PrepareScriptExport(bool srpOnly, bool both)
        {
            string input = RequirePath(ScriptExportInput, "SRP／VAR 檔案／資料夾");
            string output = RequirePath(ScriptExportOutput, "文字輸出資料夾");
            PathHistory.RememberInputPath(input);
            PathHistory.RememberOutputDirectory(output);

            return () =>
            {
                List<string> files = GetScriptBinaryFiles(input, srpOnly, both);
                if (files.Count == 0)
                {
                    throw new InvalidDataException(both
                        ? "指定位置中找不到 .srp 或 .var 檔案。"
                        : "找不到 " + (srpOnly ? "SRP" : "VAR") + " 檔案。");
                }

                bool folderMode = Directory.Exists(input);
                List<Tuple<string, byte[]>> pending = new List<Tuple<string, byte[]>>();
                HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 先解析所有來源及目的路徑；任何來源錯誤都不會先寫出部分 TXT。
                foreach (string file in files)
                {
                    string extension = Path.GetExtension(file);
                    bool isSrp = string.Equals(extension, ".srp", StringComparison.OrdinalIgnoreCase);
                    string relative = folderMode
                        ? BinaryUtil.GetRelativePathCompat(input, file)
                        : Path.GetFileName(file);
                    string outputRelative = Path.ChangeExtension(relative, isSrp ? ".srp.txt" : ".var.txt");

                    ValidateRelativeOutputPath(outputRelative);
                    if (!destinations.Add(outputRelative))
                        throw new InvalidDataException("不同來源檔案會產生相同 TXT 路徑，已停止以避免覆蓋：" + outputRelative);

                    byte[] sourceBytes = File.ReadAllBytes(file);
                    string decoded = isSrp
                        ? ScriptTextCodec.DecompileSrp(sourceBytes)
                        : ScriptTextCodec.DecompileVar(sourceBytes);
                    pending.Add(Tuple.Create(outputRelative, new UTF8Encoding(false).GetBytes(decoded)));
                }

                CommitBatchNoOverwrite(output, pending);

                foreach (Tuple<string, byte[]> item in pending)
                    LogLine("SRP／VAR → TXT：" + item.Item1);
                LogLine("批次匯出完成：" + pending.Count + " 個檔案。輸出既有檔案不會被覆蓋。");
            };
        }

        private Action PrepareScriptBuild(bool srp)
        {
            string input = RequirePath(ScriptBuildInput, "腳本文字檔／資料夾");
            string output = RequirePath(ScriptBuildOutput, "腳本輸出資料夾");
            PathHistory.RememberInputPath(input);
            PathHistory.RememberOutputDirectory(output);

            return () =>
            {
                List<string> files = GetScriptTextFilesEnhanced(input, srp);
                if (files.Count == 0)
                    throw new InvalidDataException("找不到可辨識的 " + (srp ? "SRP" : "VAR") +
                        " 文字檔。請確認副檔名、格式標頭或舊版 Type／Flags／Data 結構。");

                bool folderMode = Directory.Exists(input);
                List<Tuple<string, byte[]>> pending = new List<Tuple<string, byte[]>>();
                HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string resourceExtension = srp ? ".srp" : ".var";

                // 先完成整批文字解析與二進位編譯，再開始寫入。
                foreach (string file in files)
                {
                    string relative = folderMode
                        ? BinaryUtil.GetRelativePathCompat(input, file)
                        : Path.GetFileName(file);
                    string outputRelative = GetCompiledResourcePath(relative, resourceExtension);
                    ValidateRelativeOutputPath(outputRelative);

                    if (!destinations.Add(outputRelative))
                        throw new InvalidDataException("不同 TXT 來源會產生相同二進位輸出路徑：" + outputRelative);

                    string sourceText = ReadScriptTextForCompile(file);
                    byte[] compiled = srp
                        ? ScriptTextCodec.CompileSrp(sourceText)
                        : ScriptTextCodec.CompileVar(sourceText);
                    pending.Add(Tuple.Create(outputRelative, compiled));
                }

                CommitBatchNoOverwrite(output, pending);

                foreach (Tuple<string, byte[]> item in pending)
                    LogLine("TXT → " + (srp ? "SRP：" : "VAR：") + item.Item1);
                LogLine("回封完成：" + pending.Count + " 個檔案。輸出既有檔案不會被覆蓋。");
            };
        }

        private static List<string> GetScriptBinaryFiles(string path, bool srpOnly, bool both)
        {
            if (File.Exists(path))
            {
                string extension = Path.GetExtension(path);
                bool isSrp = string.Equals(extension, ".srp", StringComparison.OrdinalIgnoreCase);
                bool isVar = string.Equals(extension, ".var", StringComparison.OrdinalIgnoreCase);
                bool accepted = both ? (isSrp || isVar) : (srpOnly ? isSrp : isVar);
                if (!accepted)
                    throw new InvalidDataException("請選擇符合目前功能的 .srp 或 .var 檔案：" + path);
                return new List<string> { path };
            }

            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException("找不到 SRP／VAR 輸入檔案或資料夾：" + path);

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(file =>
                {
                    string extension = Path.GetExtension(file);
                    bool isSrp = string.Equals(extension, ".srp", StringComparison.OrdinalIgnoreCase);
                    bool isVar = string.Equals(extension, ".var", StringComparison.OrdinalIgnoreCase);
                    return both ? (isSrp || isVar) : (srpOnly ? isSrp : isVar);
                })
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> GetScriptTextFilesEnhanced(string path, bool srp)
        {
            if (File.Exists(path))
            {
                if (!string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("腳本來源必須是 .txt：" + path);
                return new List<string> { path };
            }

            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException("找不到文字檔資料夾：" + path);

            string v1Header = srp ? "TMRHIRO-SRP-TEXT/1" : "TMRHIRO-VAR-TEXT/1";
            string v2Header = srp ? "TMRHIRO-SRP-TEXT/2" : "TMRHIRO-VAR-TEXT/2";
            string suffix = srp ? ".srp.txt" : ".var.txt";
            List<string> results = new List<string>();

            foreach (string file in Directory.EnumerateFiles(path, "*.txt", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                    HasTextHeaderEnhanced(file, v1Header) ||
                    HasTextHeaderEnhanced(file, v2Header) ||
                    (srp ? IsLegacySrpTextEnhanced(file) : IsLegacyVarTextEnhanced(file)))
                {
                    results.Add(file);
                }
            }

            return results.OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool HasTextHeaderEnhanced(string path, string marker)
        {
            try
            {
                using (StreamReader reader = new StreamReader(path, BinaryUtil.Utf8Strict, true))
                    return string.Equals((reader.ReadLine() ?? string.Empty).Trim(), marker, StringComparison.Ordinal);
            }
            catch
            {
                try
                {
                    using (StreamReader reader = new StreamReader(path, BinaryUtil.ShiftJis, true))
                        return string.Equals((reader.ReadLine() ?? string.Empty).Trim(), marker, StringComparison.Ordinal);
                }
                catch { return false; }
            }
        }

        private static bool IsLegacySrpTextEnhanced(string path)
        {
            try
            {
                using (StreamReader reader = new StreamReader(path, BinaryUtil.ShiftJis, true))
                {
                    string type = reader.ReadLine() ?? string.Empty;
                    string flags = reader.ReadLine() ?? string.Empty;
                    string data = reader.ReadLine() ?? string.Empty;
                    if (!type.StartsWith("Type: ", StringComparison.Ordinal) ||
                        !flags.StartsWith("Flags: ", StringComparison.Ordinal) ||
                        !data.StartsWith("Data: ", StringComparison.Ordinal))
                        return false;

                    string[] tail = new string[3];
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        tail[0] = tail[1];
                        tail[1] = tail[2];
                        tail[2] = line;
                    }
                    return tail[0] == "Type: 16" && tail[1] == "Flags: 3" && tail[2] == "Data: ";
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLegacyVarTextEnhanced(string path)
        {
            try
            {
                string last = string.Empty;
                using (StreamReader reader = new StreamReader(path, BinaryUtil.ShiftJis, true))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                        last = line;
                }
                return string.Equals(last, "[END]", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string ReadScriptTextForCompile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool hasUtf8Bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
            int offset = hasUtf8Bom ? 3 : 0;
            byte[] content = offset == 0 ? bytes : bytes.Skip(offset).ToArray();

            // 本工具新格式（v1／v2）以 UTF-8 儲存；舊 croCKer TXT 沒有標頭，固定使用 CP932。
            // 不可只用「UTF-8 嚴格解碼是否成功」判斷，部分 CP932 位元組組合也可能碰巧是合法 UTF-8，
            // 進而把原本的日文 Shift-JIS 誤解成亂碼。
            if (hasUtf8Bom || HasKnownUtf8ScriptHeader(content))
                return BinaryUtil.Utf8Strict.GetString(content);

            return BinaryUtil.ShiftJisStrict.GetString(content);
        }

        private static bool HasKnownUtf8ScriptHeader(byte[] bytes)
        {
            int lineEnd = 0;
            while (lineEnd < bytes.Length && bytes[lineEnd] != 0x0a && bytes[lineEnd] != 0x0d)
            {
                if (bytes[lineEnd] > 0x7f)
                    return false;
                lineEnd++;
            }

            string firstLine = Encoding.ASCII.GetString(bytes, 0, lineEnd).Trim();
            return firstLine == "TMRHIRO-SRP-TEXT/1" ||
                   firstLine == "TMRHIRO-SRP-TEXT/2" ||
                   firstLine == "TMRHIRO-VAR-TEXT/1" ||
                   firstLine == "TMRHIRO-VAR-TEXT/2";
        }

        private static void ValidateRelativeOutputPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new InvalidDataException("輸出相對路徑無效：" + relativePath);

            string normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string[] parts = normalized.Split(Path.DirectorySeparatorChar);
            if (parts.Any(part => part == ".." || part == "." || string.IsNullOrWhiteSpace(part)))
                throw new InvalidDataException("輸出路徑含有不安全的目錄區段：" + relativePath);
        }

        /// <summary>
        /// 所有輸出先寫入輸出目錄旁的 staging 資料夾。
        /// 新輸出目錄以 Directory.Move 一次提交；既有輸出目錄則逐檔 Move，失敗時回滾已提交檔案。
        /// 既有檔案一律拒絕覆蓋，保護使用者已翻譯或編輯的 TXT／資源。
        /// </summary>
        private static void CommitBatchNoOverwrite(string outputRoot, List<Tuple<string, byte[]>> items)
        {
            if (items == null || items.Count == 0)
                throw new InvalidDataException("沒有可寫入的輸出資料。");

            string fullOutput = Path.GetFullPath(outputRoot);
            if (File.Exists(fullOutput))
                throw new IOException("輸出路徑目前是檔案，不是資料夾：" + fullOutput);

            string? parent = Path.GetDirectoryName(fullOutput.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
                throw new InvalidOperationException("無法判斷輸出資料夾的上層目錄。");
            Directory.CreateDirectory(parent);

            string normalizedOutput = fullOutput.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(normalizedOutput))
                throw new InvalidOperationException("輸出資料夾不能是磁碟根目錄。");
            string stageRoot = normalizedOutput + ".tmrhiro-staging-" + Guid.NewGuid().ToString("N");
            List<Tuple<string, string>> movedFiles = new List<Tuple<string, string>>();
            bool preserveStageForRecovery = false;

            try
            {
                Directory.CreateDirectory(stageRoot);
                foreach (Tuple<string, byte[]> item in items)
                {
                    ValidateRelativeOutputPath(item.Item1);
                    string stagedPath = Path.GetFullPath(Path.Combine(stageRoot, item.Item1));
                    EnsureInsideDirectory(stageRoot, stagedPath);
                    string? stagedDirectory = Path.GetDirectoryName(stagedPath);
                    if (!string.IsNullOrEmpty(stagedDirectory))
                        Directory.CreateDirectory(stagedDirectory);
                    File.WriteAllBytes(stagedPath, item.Item2);
                }

                if (!Directory.Exists(fullOutput))
                {
                    Directory.Move(stageRoot, fullOutput);
                    return;
                }

                // 先檢查整批衝突，避免已存在的翻譯檔遭覆蓋。
                foreach (Tuple<string, byte[]> item in items)
                {
                    string destination = Path.GetFullPath(Path.Combine(fullOutput, item.Item1));
                    EnsureInsideDirectory(fullOutput, destination);
                    if (File.Exists(destination) || Directory.Exists(destination))
                        throw new IOException("輸出檔案或資料夾已存在，為保護既有資料而停止：" + destination);
                }

                foreach (Tuple<string, byte[]> item in items)
                {
                    string stagedPath = Path.GetFullPath(Path.Combine(stageRoot, item.Item1));
                    string destination = Path.GetFullPath(Path.Combine(fullOutput, item.Item1));
                    string? destinationDirectory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destinationDirectory))
                        Directory.CreateDirectory(destinationDirectory);

                    File.Move(stagedPath, destination);
                    movedFiles.Add(Tuple.Create(destination, stagedPath));
                }
            }
            catch (Exception commitException)
            {
                // 若提交中途失敗，將已移動檔案移回 staging。
                // 回滾也失敗時保留 staging，避免清理流程把唯一可救回的副本刪掉。
                bool rollbackSucceeded = true;
                for (int i = movedFiles.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        string destination = movedFiles[i].Item1;
                        string stagedPath = movedFiles[i].Item2;
                        string? stagedDirectory = Path.GetDirectoryName(stagedPath);
                        if (!string.IsNullOrEmpty(stagedDirectory))
                            Directory.CreateDirectory(stagedDirectory);
                        if (File.Exists(destination) && !File.Exists(stagedPath))
                            File.Move(destination, stagedPath);
                    }
                    catch
                    {
                        rollbackSucceeded = false;
                    }
                }

                if (!rollbackSucceeded)
                {
                    preserveStageForRecovery = true;
                    throw new IOException(
                        "批次提交失敗，且自動回復未能全部完成。暫存資料保留於：" + stageRoot +
                        "。請勿刪除該資料夾，先檢查其中可救回的檔案。原始錯誤：" + commitException.Message,
                        commitException);
                }
                throw;
            }
            finally
            {
                if (!preserveStageForRecovery)
                {
                    try
                    {
                        if (Directory.Exists(stageRoot))
                            Directory.Delete(stageRoot, true);
                    }
                    catch
                    {
                        // 清理失敗不覆蓋真正的轉換或提交錯誤。
                    }
                }
            }
        }

        private static void EnsureInsideDirectory(string root, string path)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("輸出路徑超出指定資料夾：" + path);
        }
    }
}
