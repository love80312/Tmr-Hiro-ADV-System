using Microsoft.Win32;
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
    public partial class MainWindow : Window
    {
        private bool _isBusy;

        public MainWindow()
        {
            InitializeComponent();
            string defaultFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            PacExtractOutput.Text = Path.Combine(defaultFolder, "TmrHiro_PAC_Extracted");
            GrdExtractOutput.Text = Path.Combine(defaultFolder, "TmrHiro_GRD_PNG");
            GrdBuildOutput.Text = Path.Combine(defaultFolder, "TmrHiro_GRD_Built");
            ScriptExportOutput.Text = Path.Combine(defaultFolder, "TmrHiro_Script_Text");
            ScriptBuildOutput.Text = Path.Combine(defaultFolder, "TmrHiro_Script_Built");
            LogLine("TmrHiroStudio 已啟動。請先使用原始資源副本進行測試。");
        }

        private void LogLine(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => LogLine(message)));
                return;
            }
            LogBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
            LogBox.ScrollToEnd();
        }

        private async Task RunOperation(string title, Action operation)
        {
            if (_isBusy)
            {
                LogLine("略過：目前已有工作正在執行。");
                return;
            }
            _isBusy = true;
            IsEnabled = false;
            LogLine("開始：" + title);
            Exception? failure = null;
            try
            {
                await Task.Run(operation);
                LogLine("完成：" + title);
            }
            catch (Exception ex)
            {
                failure = ex;
                LogLine("失敗：" + ex.Message);
            }
            finally
            {
                IsEnabled = true;
                _isBusy = false;
            }
            if (failure != null)
                MessageBox.Show(failure.Message, title + "失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private static void PickFolder(TextBox target)
        {
            using (System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "選擇資料夾";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) target.Text = dialog.SelectedPath;
            }
        }

        private static void PickFile(TextBox target, string filter)
        {
            OpenFileDialog dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true, Multiselect = false };
            if (File.Exists(target.Text)) dialog.FileName = target.Text;
            if (dialog.ShowDialog() == true) target.Text = dialog.FileName;
        }

        private static string RequirePath(TextBox box, string label)
        {
            string value = box.Text.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("請先指定「" + label + "」。");
            return Path.GetFullPath(value);
        }

        private static List<string> GetInputFiles(string path, string extension, bool recursive = true)
        {
            if (File.Exists(path))
            {
                if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("檔案副檔名必須是 " + extension + "：" + path);
                return new List<string> { path };
            }
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("找不到輸入檔案或資料夾：" + path);
            SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(path, "*" + extension, option)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> GetScriptTextFiles(string path, bool srp)
        {
            if (File.Exists(path))
            {
                if (!string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("腳本來源必須是 .txt：" + path);
                return new List<string> { path };
            }
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("找不到文字檔資料夾：" + path);
            string marker = srp ? "TMRHIRO-SRP-TEXT/1" : "TMRHIRO-VAR-TEXT/1";
            string suffix = srp ? ".srp.txt" : ".var.txt";
            List<string> results = new List<string>();
            foreach (string file in Directory.EnumerateFiles(path, "*.txt", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(file);
                    continue;
                }
                if (HasTextHeader(file, marker) || (srp ? IsLegacySrpText(file) : IsLegacyVarText(file)))
                    results.Add(file);
            }
            return results.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool HasTextHeader(string path, string marker)
        {
            try
            {
                using (StreamReader reader = new StreamReader(path, BinaryUtil.ShiftJis, true))
                    return string.Equals((reader.ReadLine() ?? string.Empty).Trim(), marker, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        // croCKer 舊版輸出的 .txt 使用 Shift-JIS，沒有工具專用標頭及副檔名標籤。
        private static bool IsLegacySrpText(string path)
        {
            try
            {
                using (StreamReader reader = new StreamReader(path, BinaryUtil.ShiftJis, true))
                {
                    string firstType = reader.ReadLine() ?? string.Empty;
                    string firstFlags = reader.ReadLine() ?? string.Empty;
                    string firstData = reader.ReadLine() ?? string.Empty;
                    if (!firstType.StartsWith("Type: ", StringComparison.Ordinal) ||
                        !firstFlags.StartsWith("Flags: ", StringComparison.Ordinal) ||
                        !firstData.StartsWith("Data: ", StringComparison.Ordinal)) return false;
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
            catch { return false; }
        }

        private static bool IsLegacyVarText(string path)
        {
            try
            {
                string last = string.Empty;
                using (StreamReader reader = new StreamReader(path, BinaryUtil.ShiftJis, true))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null) last = line;
                }
                return string.Equals(last, "[END]", StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static string GetCompiledResourcePath(string relativePath, string extension)
        {
            string suffix = extension + ".txt";
            if (relativePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return relativePath.Substring(0, relativePath.Length - suffix.Length) + extension;
            return Path.ChangeExtension(relativePath, extension);
        }

        private static string ReadSrpTextForCompile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            byte[] content = offset == 0 ? bytes : bytes.Skip(offset).ToArray();
            try
            {
                // 新版格式為 UTF-8；舊 croCKer SRP TXT 為 Shift-JIS。先嚴格測試 UTF-8，失敗才回退 CP932。
                return new System.Text.UTF8Encoding(false, true).GetString(content);
            }
            catch (System.Text.DecoderFallbackException)
            {
                return BinaryUtil.ShiftJis.GetString(content);
            }
        }

        private void PacExtractFile_Click(object sender, RoutedEventArgs e) { PickFile(PacExtractInput, "PAC 封包 (*.pac)|*.pac|所有檔案 (*.*)|*.*"); }
        private void PacExtractFolder_Click(object sender, RoutedEventArgs e) { PickFolder(PacExtractInput); }
        private void PacExtractOutput_Click(object sender, RoutedEventArgs e) { PickFolder(PacExtractOutput); }
        private void PacPackInput_Click(object sender, RoutedEventArgs e) { PickFolder(PacPackInput); }

        private async void PacUnpack_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("PAC 解包", () =>
            {
                string input = RequirePath(PacExtractInput, "PAC 檔案／資料夾");
                string output = RequirePath(PacExtractOutput, "輸出資料夾");
                List<string> pacFiles = GetInputFiles(input, ".pac", false);
                if (pacFiles.Count == 0) throw new InvalidDataException("指定資料夾中找不到 PAC 檔案。");
                Directory.CreateDirectory(output);
                foreach (string pac in pacFiles)
                {
                    string target = Path.Combine(output, Path.GetFileNameWithoutExtension(pac));
                    PacCodec.Extract(pac, target, LogLine);
                }
            });
        }

        private async void PacPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string input = RequirePath(PacPackInput, "解包資料夾");
                SaveFileDialog dialog = new SaveFileDialog
                {
                    Filter = "PAC 封包 (*.pac)|*.pac|所有檔案 (*.*)|*.*",
                    DefaultExt = ".pac",
                    AddExtension = true,
                    FileName = new DirectoryInfo(input).Name + ".pac"
                };
                if (dialog.ShowDialog() != true) return;
                int version = 0;
                if (PacVersion.SelectedItem is ComboBoxItem item && item.Tag != null) version = Convert.ToInt32(item.Tag);
                await RunOperation("PAC 回封", () => PacCodec.Pack(input, dialog.FileName, version, LogLine));
            }
            catch (Exception ex)
            {
                LogLine("失敗：" + ex.Message);
                MessageBox.Show(ex.Message, "PAC 回封失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GrdExtractFile_Click(object sender, RoutedEventArgs e) { PickFile(GrdExtractInput, "GRD 圖像 (*.grd)|*.grd|所有檔案 (*.*)|*.*"); }
        private void GrdExtractFolder_Click(object sender, RoutedEventArgs e) { PickFolder(GrdExtractInput); }
        private void GrdExtractOutput_Click(object sender, RoutedEventArgs e) { PickFolder(GrdExtractOutput); }
        private void GrdBuildFile_Click(object sender, RoutedEventArgs e) { PickFile(GrdBuildInput, "PNG 圖像 (*.png)|*.png|所有檔案 (*.*)|*.*"); }
        private void GrdBuildFolder_Click(object sender, RoutedEventArgs e) { PickFolder(GrdBuildInput); }
        private void GrdBuildOutput_Click(object sender, RoutedEventArgs e) { PickFolder(GrdBuildOutput); }

        private async void GrdExtract_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("GRD 解碼", () =>
            {
                string input = RequirePath(GrdExtractInput, "GRD 檔案／資料夾");
                string output = RequirePath(GrdExtractOutput, "PNG 輸出資料夾");
                List<string> files = GetInputFiles(input, ".grd");
                if (files.Count == 0) throw new InvalidDataException("找不到 GRD 檔案。");
                Directory.CreateDirectory(output);
                bool folderMode = Directory.Exists(input);
                foreach (string file in files)
                {
                    string relative = folderMode ? BinaryUtil.GetRelativePathCompat(input, file) : Path.GetFileName(file);
                    string pngRelative = Path.ChangeExtension(relative, ".png");
                    string destination = Path.Combine(output, pngRelative);
                    string? directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    GrdCodec.ExtractToPng(file, destination);
                    LogLine("GRD → PNG：" + relative);
                }
            });
        }

        private async void GrdBuild_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("GRD 回封", () =>
            {
                string input = RequirePath(GrdBuildInput, "PNG 檔案／資料夾");
                string output = RequirePath(GrdBuildOutput, "GRD 輸出資料夾");
                List<string> files = GetInputFiles(input, ".png");
                if (files.Count == 0) throw new InvalidDataException("找不到 PNG 檔案。");
                Directory.CreateDirectory(output);
                bool folderMode = Directory.Exists(input);
                foreach (string file in files)
                {
                    string relative = folderMode ? BinaryUtil.GetRelativePathCompat(input, file) : Path.GetFileName(file);
                    string grdRelative = Path.ChangeExtension(relative, ".grd");
                    string destination = Path.Combine(output, grdRelative);
                    string? directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    GrdCodec.CreateFromPng(file, destination);
                    LogLine("PNG → GRD：" + relative);
                }
            });
        }

        private void ScriptExportFile_Click(object sender, RoutedEventArgs e) { PickFile(ScriptExportInput, "SRP／VAR 資源 (*.srp;*.var)|*.srp;*.var|所有檔案 (*.*)|*.*"); }
        private void ScriptExportFolder_Click(object sender, RoutedEventArgs e) { PickFolder(ScriptExportInput); }
        private void ScriptExportOutput_Click(object sender, RoutedEventArgs e) { PickFolder(ScriptExportOutput); }
        private void ScriptBuildFile_Click(object sender, RoutedEventArgs e) { PickFile(ScriptBuildInput, "文字檔 (*.txt)|*.txt|所有檔案 (*.*)|*.*"); }
        private void ScriptBuildFolder_Click(object sender, RoutedEventArgs e) { PickFolder(ScriptBuildInput); }
        private void ScriptBuildOutput_Click(object sender, RoutedEventArgs e) { PickFolder(ScriptBuildOutput); }

        private async void SrpExport_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP 匯出", () =>
            {
                string input = RequirePath(ScriptExportInput, "SRP 檔案／資料夾");
                string output = RequirePath(ScriptExportOutput, "文字輸出資料夾");
                List<string> files = GetInputFiles(input, ".srp");
                if (files.Count == 0) throw new InvalidDataException("找不到 SRP 檔案。");
                Directory.CreateDirectory(output);
                bool folderMode = Directory.Exists(input);
                foreach (string file in files)
                {
                    string relative = folderMode ? BinaryUtil.GetRelativePathCompat(input, file) : Path.GetFileName(file);
                    string targetRelative = Path.ChangeExtension(relative, ".srp.txt");
                    string destination = Path.Combine(output, targetRelative);
                    string? directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    JsonUtil.WriteUtf8Text(destination, ScriptCodec.DecompileSrp(File.ReadAllBytes(file)));
                    LogLine("SRP → TXT：" + relative);
                }
            });
        }

        private async void VarExport_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("VAR 匯出", () =>
            {
                string input = RequirePath(ScriptExportInput, "VAR 檔案／資料夾");
                string output = RequirePath(ScriptExportOutput, "文字輸出資料夾");
                List<string> files = GetInputFiles(input, ".var");
                if (files.Count == 0) throw new InvalidDataException("找不到 VAR 檔案。");
                Directory.CreateDirectory(output);
                bool folderMode = Directory.Exists(input);
                foreach (string file in files)
                {
                    string relative = folderMode ? BinaryUtil.GetRelativePathCompat(input, file) : Path.GetFileName(file);
                    string destination = Path.Combine(output, Path.ChangeExtension(relative, ".var.txt"));
                    string? directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    JsonUtil.WriteUtf8Text(destination, ScriptCodec.DecompileVar(File.ReadAllBytes(file)));
                    LogLine("VAR → TXT：" + relative);
                }
            });
        }

        private async void SrpBuild_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP 回封", () =>
            {
                string input = RequirePath(ScriptBuildInput, "SRP 文字檔／資料夾");
                string output = RequirePath(ScriptBuildOutput, "SRP 輸出資料夾");
                List<string> files = GetScriptTextFiles(input, true);
                if (files.Count == 0) throw new InvalidDataException("找不到 SRP 文字檔。可辨識 .srp.txt、新格式標頭，以及 croCKer 舊版 Type／Flags／Data 文字。");
                Directory.CreateDirectory(output);
                bool folderMode = Directory.Exists(input);
                foreach (string file in files)
                {
                    string relative = folderMode ? BinaryUtil.GetRelativePathCompat(input, file) : Path.GetFileName(file);
                    string destination = Path.Combine(output, GetCompiledResourcePath(relative, ".srp"));
                    string? directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    byte[] compiled = ScriptCodec.CompileSrp(ReadSrpTextForCompile(file));
                    BinaryUtil.WriteAtomically(destination, compiled);
                    LogLine("TXT → SRP：" + relative);
                }
            });
        }

        private async void VarBuild_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("VAR 回封", () =>
            {
                string input = RequirePath(ScriptBuildInput, "VAR 文字檔／資料夾");
                string output = RequirePath(ScriptBuildOutput, "VAR 輸出資料夾");
                List<string> files = GetScriptTextFiles(input, false);
                if (files.Count == 0) throw new InvalidDataException("找不到 VAR 文字檔。可辨識 .var.txt、新格式標頭，以及以 [END] 結尾的舊版文字。");
                Directory.CreateDirectory(output);
                bool folderMode = Directory.Exists(input);
                foreach (string file in files)
                {
                    string relative = folderMode ? BinaryUtil.GetRelativePathCompat(input, file) : Path.GetFileName(file);
                    string destination = Path.Combine(output, GetCompiledResourcePath(relative, ".var"));
                    string? directory = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    byte[] compiled = ScriptCodec.CompileVar(JsonUtil.ReadUtf8Text(file));
                    BinaryUtil.WriteAtomically(destination, compiled);
                    LogLine("TXT → VAR：" + relative);
                }
            });
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e) { LogBox.Clear(); }
    }
}
