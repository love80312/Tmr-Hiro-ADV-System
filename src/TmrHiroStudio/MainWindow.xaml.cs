using Microsoft.Win32;
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
    public partial class MainWindow : Window
    {
        private bool _isBusy;

        public MainWindow()
        {
            InitializeComponent();

            // 免安裝版預設將輸出放在程式目錄下的 Output，方便集中管理。
            // 實際執行時才建立資料夾；程式啟動不會修改磁碟內容。
            string outputRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");
            PacExtractOutput.Text = Path.Combine(outputRoot, "PAC_Extracted");
            GrdExtractOutput.Text = Path.Combine(outputRoot, "GRD_PNG");
            GrdBuildOutput.Text = Path.Combine(outputRoot, "GRD_Built");
            ScriptExportOutput.Text = Path.Combine(outputRoot, "Script_Text");
            ScriptBuildOutput.Text = Path.Combine(outputRoot, "Script_Built");

            LogLine("TmrHiroStudio 已啟動。請先使用原始資源副本進行測試。");
            LogLine("檔案／資料夾選擇會優先使用目前欄位路徑，其次使用上次路徑，最後使用程式所在目錄。");
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

        /// <summary>
        /// prepareOperation 必須在 UI 執行緒讀取 WPF 控制項，並回傳只使用一般資料的背景工作。
        /// Task.Run 內禁止讀取 TextBox、ComboBox 或其他 DispatcherObject。
        /// </summary>
        private async Task RunOperation(string title, Func<Action> prepareOperation)
        {
            if (_isBusy)
            {
                LogLine("略過：目前已有工作正在執行。");
                return;
            }

            Action operation;
            try
            {
                // 此段在 UI 執行緒執行：擷取並驗證所有欄位內容。
                operation = prepareOperation();
                if (operation == null)
                    throw new InvalidOperationException("沒有建立可執行的工作。");
            }
            catch (Exception ex)
            {
                LogLine("準備失敗：" + ex);
                MessageBox.Show(ex.Message, title + "失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "準備失敗";
                return;
            }

            _isBusy = true;
            MainTabs.IsEnabled = false;
            BusyProgress.Visibility = Visibility.Visible;
            StatusText.Text = "處理中：" + title;
            LogLine("開始：" + title);

            try
            {
                await Task.Run(operation);
                LogLine("完成：" + title);
                StatusText.Text = "完成：" + title;
            }
            catch (Exception ex)
            {
                // 記錄完整例外，便於使用者回報；對話框只顯示主要訊息。
                LogLine("失敗：" + ex);
                StatusText.Text = "失敗：" + title;
                MessageBox.Show(ex.Message, title + "失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                MainTabs.IsEnabled = true;
                BusyProgress.Visibility = Visibility.Collapsed;
                _isBusy = false;
            }
        }

        private void PickFolder(TextBox target, string description)
        {
            try
            {
                string initialDirectory = PathHistory.GetInitialDirectory(target.Text, true);
                string? selected = NativeFolderPicker.Show(this, description, initialDirectory);
                if (selected == null || string.IsNullOrWhiteSpace(selected)) return;

                target.Text = selected;
                PathHistory.RememberFolderSelection(selected);
                StatusText.Text = "已選擇資料夾";
            }
            catch (Exception ex)
            {
                LogLine("資料夾選擇失敗：" + ex);
                MessageBox.Show(ex.Message, "選擇資料夾失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PickFile(TextBox target, string filter)
        {
            try
            {
                OpenFileDialog dialog = new OpenFileDialog
                {
                    Filter = filter,
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false,
                    RestoreDirectory = true,
                    InitialDirectory = PathHistory.GetInitialDirectory(target.Text, false)
                };

                if (File.Exists(target.Text))
                    dialog.FileName = Path.GetFileName(target.Text);

                if (dialog.ShowDialog(this) != true) return;

                target.Text = dialog.FileName;
                PathHistory.RememberFileSelection(dialog.FileName);
                StatusText.Text = "已選擇檔案";
            }
            catch (Exception ex)
            {
                LogLine("檔案選擇失敗：" + ex);
                MessageBox.Show(ex.Message, "選擇檔案失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string RequirePath(TextBox box, string label)
        {
            // 本方法只可由 UI 執行緒呼叫；RunOperation 會在 Task.Run 前完成擷取。
            string value = (box.Text ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("請先指定「" + label + "」。");
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

            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException("找不到輸入檔案或資料夾：" + path);

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

            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException("找不到文字檔資料夾：" + path);

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
            catch
            {
                return false;
            }
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
                        !firstData.StartsWith("Data: ", StringComparison.Ordinal))
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
            catch
            {
                return false;
            }
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
                return new UTF8Encoding(false, true).GetString(content);
            }
            catch (DecoderFallbackException)
            {
                return BinaryUtil.ShiftJis.GetString(content);
            }
        }

        private void PacExtractFile_Click(object sender, RoutedEventArgs e)
        {
            PickFile(PacExtractInput, "PAC 封包 (*.pac)|*.pac|所有檔案 (*.*)|*.*");
        }

        private void PacExtractFolder_Click(object sender, RoutedEventArgs e) { PickFolder(PacExtractInput, "選擇 PAC 輸入資料夾"); }
        private void PacExtractOutput_Click(object sender, RoutedEventArgs e) { PickFolder(PacExtractOutput, "選擇 PAC 輸出資料夾"); }
        private void PacPackInput_Click(object sender, RoutedEventArgs e) { PickFolder(PacPackInput, "選擇 PAC 解包資料夾"); }

        private async void PacUnpack_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("PAC 解包", () =>
            {
                string input = RequirePath(PacExtractInput, "PAC 檔案／資料夾");
                string output = RequirePath(PacExtractOutput, "輸出資料夾");
                PathHistory.RememberInputPath(input);
                PathHistory.RememberOutputDirectory(output);

                return () =>
                {
                    List<string> pacFiles = GetInputFiles(input, ".pac", false);
                    if (pacFiles.Count == 0)
                        throw new InvalidDataException("指定資料夾中找不到 PAC 檔案。");

                    Directory.CreateDirectory(output);
                    foreach (string pac in pacFiles)
                    {
                        string target = Path.Combine(output, Path.GetFileNameWithoutExtension(pac));
                        PacCodec.Extract(pac, target, LogLine);
                    }
                };
            });
        }

        private async void PacPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 在 UI 執行緒擷取路徑及版本，對話框也必須由 UI 執行緒顯示。
                string input = RequirePath(PacPackInput, "解包資料夾");
                PathHistory.RememberInputPath(input);

                int version = 0;
                if (PacVersion.SelectedItem is ComboBoxItem item && item.Tag != null)
                    version = Convert.ToInt32(item.Tag);

                SaveFileDialog dialog = new SaveFileDialog
                {
                    Filter = "PAC 封包 (*.pac)|*.pac|所有檔案 (*.*)|*.*",
                    DefaultExt = ".pac",
                    AddExtension = true,
                    OverwritePrompt = true,
                    RestoreDirectory = true,
                    InitialDirectory = PathHistory.GetInitialDirectory(input, true),
                    FileName = new DirectoryInfo(input).Name + ".pac"
                };

                if (dialog.ShowDialog(this) != true) return;
                string output = Path.GetFullPath(dialog.FileName);
                PathHistory.RememberFileSelection(output);

                await RunOperation("PAC 回封", () => () => PacCodec.Pack(input, output, version, LogLine));
            }
            catch (Exception ex)
            {
                LogLine("PAC 回封準備失敗：" + ex);
                MessageBox.Show(ex.Message, "PAC 回封失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GrdExtractFile_Click(object sender, RoutedEventArgs e) { PickFile(GrdExtractInput, "GRD 圖像 (*.grd)|*.grd|所有檔案 (*.*)|*.*"); }
        private void GrdExtractFolder_Click(object sender, RoutedEventArgs e) { PickFolder(GrdExtractInput, "選擇 GRD 輸入資料夾"); }
        private void GrdExtractOutput_Click(object sender, RoutedEventArgs e) { PickFolder(GrdExtractOutput, "選擇 PNG 輸出資料夾"); }
        private void GrdBuildFile_Click(object sender, RoutedEventArgs e) { PickFile(GrdBuildInput, "PNG 圖像 (*.png)|*.png|所有檔案 (*.*)|*.*"); }
        private void GrdBuildFolder_Click(object sender, RoutedEventArgs e) { PickFolder(GrdBuildInput, "選擇 PNG 輸入資料夾"); }
        private void GrdBuildOutput_Click(object sender, RoutedEventArgs e) { PickFolder(GrdBuildOutput, "選擇 GRD 輸出資料夾"); }

        private async void GrdExtract_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("GRD 解碼", () =>
            {
                string input = RequirePath(GrdExtractInput, "GRD 檔案／資料夾");
                string output = RequirePath(GrdExtractOutput, "PNG 輸出資料夾");
                PathHistory.RememberInputPath(input);
                PathHistory.RememberOutputDirectory(output);

                return () =>
                {
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
                };
            });
        }

        private async void GrdBuild_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("GRD 回封", () =>
            {
                string input = RequirePath(GrdBuildInput, "PNG 檔案／資料夾");
                string output = RequirePath(GrdBuildOutput, "GRD 輸出資料夾");
                PathHistory.RememberInputPath(input);
                PathHistory.RememberOutputDirectory(output);

                return () =>
                {
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
                };
            });
        }

        private void ScriptExportFile_Click(object sender, RoutedEventArgs e) { PickFile(ScriptExportInput, "SRP／VAR 資源 (*.srp;*.var)|*.srp;*.var|所有檔案 (*.*)|*.*"); }
        private void ScriptExportFolder_Click(object sender, RoutedEventArgs e) { PickFolder(ScriptExportInput, "選擇 SRP／VAR 輸入資料夾"); }
        private void ScriptExportOutput_Click(object sender, RoutedEventArgs e) { PickFolder(ScriptExportOutput, "選擇文字輸出資料夾"); }
        private void ScriptBuildFile_Click(object sender, RoutedEventArgs e) { PickFile(ScriptBuildInput, "文字檔 (*.txt)|*.txt|所有檔案 (*.*)|*.*"); }
        private void ScriptBuildFolder_Click(object sender, RoutedEventArgs e) { PickFolder(ScriptBuildInput, "選擇 TXT 輸入資料夾"); }
        private void ScriptBuildOutput_Click(object sender, RoutedEventArgs e) { PickFolder(ScriptBuildOutput, "選擇二進位輸出資料夾"); }

        private async void SrpExport_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP 匯出", () =>
            {
                string input = RequirePath(ScriptExportInput, "SRP 檔案／資料夾");
                string output = RequirePath(ScriptExportOutput, "文字輸出資料夾");
                PathHistory.RememberInputPath(input);
                PathHistory.RememberOutputDirectory(output);

                return () =>
                {
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
                };
            });
        }

        private async void VarExport_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("VAR 匯出", () =>
            {
                string input = RequirePath(ScriptExportInput, "VAR 檔案／資料夾");
                string output = RequirePath(ScriptExportOutput, "文字輸出資料夾");
                PathHistory.RememberInputPath(input);
                PathHistory.RememberOutputDirectory(output);

                return () =>
                {
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
                };
            });
        }

        private async void SrpBuild_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP 回封", () =>
            {
                string input = RequirePath(ScriptBuildInput, "SRP 文字檔／資料夾");
                string output = RequirePath(ScriptBuildOutput, "SRP 輸出資料夾");
                PathHistory.RememberInputPath(input);
                PathHistory.RememberOutputDirectory(output);

                return () =>
                {
                    List<string> files = GetScriptTextFiles(input, true);
                    if (files.Count == 0)
                        throw new InvalidDataException("找不到 SRP 文字檔。可辨識 .srp.txt、新格式標頭，以及 croCKer 舊版 Type／Flags／Data 文字。");
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
                };
            });
        }

        private async void VarBuild_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("VAR 回封", () =>
            {
                string input = RequirePath(ScriptBuildInput, "VAR 文字檔／資料夾");
                string output = RequirePath(ScriptBuildOutput, "VAR 輸出資料夾");
                PathHistory.RememberInputPath(input);
                PathHistory.RememberOutputDirectory(output);

                return () =>
                {
                    List<string> files = GetScriptTextFiles(input, false);
                    if (files.Count == 0)
                        throw new InvalidDataException("找不到 VAR 文字檔。可辨識 .var.txt、新格式標頭，以及以 [END] 結尾的舊版文字。");
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
                };
            });
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogBox.Clear();
        }
    }
}
