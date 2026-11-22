using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TmrHiroStudio.Core;

namespace TmrHiroStudio
{
    /// <summary>
    /// PAC／GRD 批次操作保護。
    /// 每批先在輸出資料夾旁的工作目錄完成全部解碼／編碼，
    /// 再以目錄／檔案移動方式提交；既有輸出不會被覆蓋。
    /// </summary>
    public partial class MainWindow
    {
        private sealed class BatchRecoveryException : IOException
        {
            public BatchRecoveryException(string message, Exception innerException)
                : base(message, innerException) { }
        }

        private async void PacUnpackEnhanced_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("PAC 解包", () => PreparePacUnpackEnhanced());
        }

        private Action PreparePacUnpackEnhanced()
        {
            string input = RequirePath(PacExtractInput, "PAC 檔案／資料夾");
            string output = RequirePath(PacExtractOutput, "PAC 輸出資料夾");
            PathHistory.RememberInputPath(input);
            PathHistory.RememberOutputDirectory(output);

            return () =>
            {
                List<string> pacFiles = GetInputFiles(input, ".pac", false);
                if (pacFiles.Count == 0)
                    throw new InvalidDataException("指定位置中找不到 PAC 檔案。");

                string workDirectory = CreateWorkDirectoryBeside(output);
                bool preserveWorkDirectory = false;
                try
                {
                    foreach (string pac in pacFiles)
                    {
                        string folderName = BinaryUtil.SafeLeafName(Path.GetFileNameWithoutExtension(pac));
                        string target = Path.Combine(workDirectory, folderName);
                        PacCodec.Extract(pac, target, LogLine);
                    }

                    int outputCount = CountStagedFiles(workDirectory);
                    try
                    {
                        CommitStagedDirectoryNoOverwrite(output, workDirectory);
                    }
                    catch (BatchRecoveryException)
                    {
                        preserveWorkDirectory = true;
                        throw;
                    }
                    LogLine("PAC 批次解包完成：" + pacFiles.Count + " 個封包、" + outputCount + " 個輸出檔案。既有輸出未覆蓋。");
                }
                finally
                {
                    if (!preserveWorkDirectory)
                        TryDeleteWorkDirectory(workDirectory);
                }
            };
        }

        private async void GrdExtractEnhanced_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("GRD 解碼", () => PrepareGrdExtractEnhanced());
        }

        private Action PrepareGrdExtractEnhanced()
        {
            string input = RequirePath(GrdExtractInput, "GRD 檔案／資料夾");
            string output = RequirePath(GrdExtractOutput, "PNG 輸出資料夾");
            PathHistory.RememberInputPath(input);
            PathHistory.RememberOutputDirectory(output);

            return () =>
            {
                List<string> files = GetInputFiles(input, ".grd");
                if (files.Count == 0)
                    throw new InvalidDataException("找不到 GRD 檔案。");

                bool folderMode = Directory.Exists(input);
                string workDirectory = CreateWorkDirectoryBeside(output);
                bool preserveWorkDirectory = false;
                try
                {
                    foreach (string file in files)
                    {
                        string relative = folderMode
                            ? BinaryUtil.GetRelativePathCompat(input, file)
                            : Path.GetFileName(file);
                        string relativePng = Path.ChangeExtension(relative, ".png");
                        string stagedPng = Path.Combine(workDirectory, relativePng);
                        string? stagedParent = Path.GetDirectoryName(stagedPng);
                        if (!string.IsNullOrEmpty(stagedParent))
                            Directory.CreateDirectory(stagedParent);

                        GrdCodec.ExtractToPng(file, stagedPng);
                        LogLine("GRD → PNG（暫存完成）：「" + relative + "」");
                    }

                    int outputCount = CountStagedFiles(workDirectory);
                    try
                    {
                        CommitStagedDirectoryNoOverwrite(output, workDirectory);
                    }
                    catch (BatchRecoveryException)
                    {
                        preserveWorkDirectory = true;
                        throw;
                    }
                    LogLine("GRD 批次解碼完成：" + files.Count + " 張圖像，含 sidecar 共 " + outputCount + " 個檔案。既有輸出未覆蓋。");
                }
                finally
                {
                    if (!preserveWorkDirectory)
                        TryDeleteWorkDirectory(workDirectory);
                }
            };
        }

        private async void GrdBuildEnhanced_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("GRD 回封", () => PrepareGrdBuildEnhanced());
        }

        private Action PrepareGrdBuildEnhanced()
        {
            string input = RequirePath(GrdBuildInput, "PNG 檔案／資料夾");
            string output = RequirePath(GrdBuildOutput, "GRD 輸出資料夾");
            PathHistory.RememberInputPath(input);
            PathHistory.RememberOutputDirectory(output);

            return () =>
            {
                List<string> files = GetInputFiles(input, ".png");
                if (files.Count == 0)
                    throw new InvalidDataException("找不到 PNG 檔案。");

                bool folderMode = Directory.Exists(input);
                string workDirectory = CreateWorkDirectoryBeside(output);
                bool preserveWorkDirectory = false;
                try
                {
                    foreach (string file in files)
                    {
                        string relative = folderMode
                            ? BinaryUtil.GetRelativePathCompat(input, file)
                            : Path.GetFileName(file);
                        string relativeGrd = Path.ChangeExtension(relative, ".grd");
                        string stagedGrd = Path.Combine(workDirectory, relativeGrd);
                        string? stagedParent = Path.GetDirectoryName(stagedGrd);
                        if (!string.IsNullOrEmpty(stagedParent))
                            Directory.CreateDirectory(stagedParent);

                        GrdCodec.CreateFromPng(file, stagedGrd);
                        LogLine("PNG → GRD（暫存完成）：「" + relative + "」");
                    }

                    int outputCount = CountStagedFiles(workDirectory);
                    try
                    {
                        CommitStagedDirectoryNoOverwrite(output, workDirectory);
                    }
                    catch (BatchRecoveryException)
                    {
                        preserveWorkDirectory = true;
                        throw;
                    }
                    LogLine("GRD 批次回封完成：" + files.Count + " 個來源、" + outputCount + " 個輸出檔案。既有輸出未覆蓋。");
                }
                finally
                {
                    if (!preserveWorkDirectory)
                        TryDeleteWorkDirectory(workDirectory);
                }
            };
        }

        private static string CreateWorkDirectoryBeside(string outputDirectory)
        {
            string fullOutput = Path.GetFullPath(outputDirectory);
            string? parent = Path.GetDirectoryName(fullOutput.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
                throw new InvalidOperationException("無法在指定輸出路徑旁建立安全的批次暫存目錄。");

            Directory.CreateDirectory(parent);
            string workDirectory = Path.Combine(
                parent,
                Path.GetFileName(fullOutput.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) +
                ".tmrhiro-work-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDirectory);
            return workDirectory;
        }

        private static int CountStagedFiles(string root)
        {
            int count = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count();
            if (count == 0)
                throw new InvalidDataException("批次處理沒有產生任何輸出檔案。");
            return count;
        }

        /// <summary>
        /// 將完整工作目錄提交到輸出位置，不把所有圖片／封包資源載入 RAM。
        /// 若目的目錄已存在，先完成整批衝突檢查，再逐檔移動；提交失敗時回滾。
        /// </summary>
        private static void CommitStagedDirectoryNoOverwrite(string outputRoot, string workRoot)
        {
            string fullOutput = Path.GetFullPath(outputRoot);
            string fullWork = Path.GetFullPath(workRoot);
            if (File.Exists(fullOutput))
                throw new IOException("輸出路徑目前是檔案，不是資料夾：" + fullOutput);

            if (!Directory.Exists(fullOutput))
            {
                Directory.Move(fullWork, fullOutput);
                return;
            }

            List<Tuple<string, string>> files = new List<Tuple<string, string>>();
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string source in Directory.EnumerateFiles(fullWork, "*", SearchOption.AllDirectories))
            {
                string relative = BinaryUtil.GetRelativePathCompat(fullWork, source);
                ValidateRelativeOutputPath(relative);
                if (!unique.Add(relative))
                    throw new InvalidDataException("批次輸出產生大小寫不敏感的重複路徑：" + relative);
                string destination = Path.GetFullPath(Path.Combine(fullOutput, relative));
                EnsureInsideDirectory(fullOutput, destination);
                if (File.Exists(destination) || Directory.Exists(destination))
                    throw new IOException("輸出檔案或資料夾已存在，為保護既有資料而停止：" + destination);
                files.Add(Tuple.Create(source, destination));
            }

            if (files.Count == 0)
                throw new InvalidDataException("批次處理沒有產生任何輸出檔案。");

            List<Tuple<string, string>> moved = new List<Tuple<string, string>>();
            try
            {
                foreach (Tuple<string, string> item in files)
                {
                    string? directory = Path.GetDirectoryName(item.Item2);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    File.Move(item.Item1, item.Item2);
                    moved.Add(Tuple.Create(item.Item2, item.Item1));
                }
            }
            catch (Exception commitException)
            {
                bool rollbackSucceeded = true;
                for (int i = moved.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        string destination = moved[i].Item1;
                        string source = moved[i].Item2;
                        string? sourceDirectory = Path.GetDirectoryName(source);
                        if (!string.IsNullOrEmpty(sourceDirectory))
                            Directory.CreateDirectory(sourceDirectory);
                        if (File.Exists(destination) && !File.Exists(source))
                            File.Move(destination, source);
                    }
                    catch
                    {
                        rollbackSucceeded = false;
                    }
                }

                if (!rollbackSucceeded)
                {
                    throw new BatchRecoveryException(
                        "批次提交失敗且自動回復未能全部完成。未移動完的資料保留於暫存目錄：" + fullWork +
                        "。請勿刪除該資料夾，先檢查可救回的檔案。原始錯誤：" + commitException.Message,
                        commitException);
                }
                throw;
            }
        }

        private static void TryDeleteWorkDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
                // 保留無法刪除的工作目錄，避免清理例外遮蔽主要處理錯誤。
            }
        }
    }
}
