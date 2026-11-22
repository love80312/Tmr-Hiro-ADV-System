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
    /// SRP／VAR 增強功能：以單一轉換器處理單獨匯出、合併匯出及回封。
    /// 透過 partial class 保留原有 MainWindow.xaml.cs，不必重複整份 GUI 程式碼。
    /// </summary>
    public partial class MainWindow
    {
        private bool _scriptEnhancementsInstalled;

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (_scriptEnhancementsInstalled) return;

            try
            {
                TabItem? scriptTab = MainTabs.Items.OfType<TabItem>()
                    .FirstOrDefault(item => string.Equals(Convert.ToString(item.Header), "SRP／VAR 腳本", StringComparison.Ordinal));
                if (scriptTab == null || !(scriptTab.Content is DependencyObject tabContent))
                    throw new InvalidOperationException("找不到 SRP／VAR 腳本分頁。");

                StackPanel? exportPanel = FindLogicalDescendant<StackPanel>(tabContent,
                    panel => FindButton(panel, "SRP → TXT") != null && FindButton(panel, "VAR → TXT") != null);
                if (exportPanel == null)
                    throw new InvalidOperationException("找不到 SRP／VAR 匯出按鈕列。");

                Button? srpExport = FindButton(exportPanel, "SRP → TXT");
                Button? varExport = FindButton(exportPanel, "VAR → TXT");
                Button? srpBuild = FindButton(tabContent, "TXT → SRP");
                Button? varBuild = FindButton(tabContent, "TXT → VAR");
                if (srpExport == null || varExport == null || srpBuild == null || varBuild == null)
                    throw new InvalidOperationException("找不到完整的 SRP／VAR 操作按鈕。");

                // 將現有四個按鈕統一切換至支援 EUDC 對照及 Type 80 保護的新轉換器。
                srpExport.Click -= SrpExport_Click;
                srpExport.Click += SrpExportMapped_Click;
                varExport.Click -= VarExport_Click;
                varExport.Click += VarExportMapped_Click;
                srpBuild.Click -= SrpBuild_Click;
                srpBuild.Click += SrpBuildMapped_Click;
                varBuild.Click -= VarBuild_Click;
                varBuild.Click += VarBuildMapped_Click;

                if (FindButton(exportPanel, "SRP + VAR → TXT") == null)
                {
                    Button bothButton = new Button
                    {
                        Content = "SRP + VAR → TXT",
                        Margin = new Thickness(4),
                        Padding = new Thickness(12, 5, 12, 5),
                        ToolTip = "遞迴搜尋輸入資料夾內所有 .srp 與 .var，保留原始子資料夾結構。"
                    };
                    bothButton.Click += BothExport_Click;
                    int insertAt = exportPanel.Children.IndexOf(varExport) + 1;
                    exportPanel.Children.Insert(insertAt, bothButton);
                }

                _scriptEnhancementsInstalled = true;
                LogLine("SRP／VAR 增強功能已啟用：合併批次匯出、CP932 私用字元對照及 Type 80 原始資料保護。");
            }
            catch (Exception ex)
            {
                LogLine("SRP／VAR 增強功能初始化失敗：" + ex);
            }
        }

        private static T? FindLogicalDescendant<T>(DependencyObject root, Func<T, bool> predicate)
            where T : DependencyObject
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (!(child is DependencyObject dependencyObject)) continue;
                if (dependencyObject is T candidate && predicate(candidate)) return candidate;
                T? nested = FindLogicalDescendant<T>(dependencyObject, predicate);
                if (nested != null) return nested;
            }
            return null;
        }

        private static Button? FindButton(DependencyObject root, string content)
        {
            return FindLogicalDescendant<Button>(root,
                button => string.Equals(Convert.ToString(button.Content), content, StringComparison.Ordinal));
        }

        private async void SrpExportMapped_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP 匯出", () => PrepareScriptExport(true, false));
        }

        private async void VarExportMapped_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("VAR 匯出", () => PrepareScriptExport(false, false));
        }

        private async void BothExport_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP + VAR 批次匯出", () => PrepareScriptExport(false, true));
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
                    throw new InvalidDataException(both
                        ? "指定位置中找不到 .srp 或 .var 檔案。"
                        : "找不到 " + (srpOnly ? "SRP" : "VAR") + " 檔案。");

                bool folderMode = Directory.Exists(input);
                List<Tuple<string, byte[]>> pending = new List<Tuple<string, byte[]>>();
                HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 先完成全部解析及目標路徑檢查，避免中途遇到壞檔時留下半套結果。
                foreach (string file in files)
                {
                    string extension = Path.GetExtension(file);
                    bool isSrp = string.Equals(extension, ".srp", StringComparison.OrdinalIgnoreCase);
                    string relative = folderMode ? BinaryUtil.GetRelativePathCompat(input, file) : Path.GetFileName(file);
                    string outputRelative = Path.ChangeExtension(relative, isSrp ? ".srp.txt" : ".var.txt");
                    if (!destinations.Add(outputRelative))
                        throw new InvalidDataException("不同來源檔案會產生相同 TXT 路徑，已停止以避免覆蓋：" + outputRelative);

                    byte[] sourceBytes = File.ReadAllBytes(file);
                    string decoded = isSrp
                        ? ScriptTextCodec.DecompileSrp(sourceBytes)
                        : ScriptTextCodec.DecompileVar(sourceBytes);
                    pending.Add(Tuple.Create(outputRelative, new UTF8Encoding(false).GetBytes(decoded)));
                }

                Directory.CreateDirectory(output);
                foreach (Tuple<string, byte[]> item in pending)
                {
                    string destination = Path.Combine(output, item.Item1);
                    BinaryUtil.WriteAtomically(destination, item.Item2);
                    LogLine((item.Item1.EndsWith(".srp.txt", StringComparison.OrdinalIgnoreCase) ? "SRP" : "VAR") +
                            " → TXT：" + item.Item1);
                }
                LogLine("批次匯出完成：" + pending.Count + " 個檔案。");
            };
        }

        private static List<string> GetScriptBinaryFiles(string path, bool srpOnly, bool both)
        {
            if (File.Exists(path))
            {
                string extension = Path.GetExtension(path);
                bool isSrp = string.Equals(extension, ".srp", StringComparison.OrdinalIgnoreCase);
                bool isVar = string.Equals(extension, ".var", StringComparison.OrdinalIgnoreCase);
                if ((srpOnly && !isSrp) || (!srpOnly && !isVar && !both) || (!isSrp && !isVar))
                    throw new InvalidDataException("請選擇 .srp 或 .var 檔案：" + path);
                return new List<string> { path };
            }

            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException("找不到 SRP／VAR 輸入檔案或資料夾：" + path);

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(file =>
                {
                    string extension = Path.GetExtension(file);
                    if (srpOnly) return string.Equals(extension, ".srp", StringComparison.OrdinalIgnoreCase);
                    if (both)
                        return string.Equals(extension, ".srp", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(extension, ".var", StringComparison.OrdinalIgnoreCase);
                    return string.Equals(extension, ".var", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async void SrpBuildMapped_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("SRP 回封", () => PrepareScriptBuild(true));
        }

        private async void VarBuildMapped_Click(object sender, RoutedEventArgs e)
        {
            await RunOperation("VAR 回封", () => PrepareScriptBuild(false));
        }

        private Action PrepareScriptBuild(bool srp)
        {
            string input = RequirePath(ScriptBuildInput, "腳本文字檔／資料夾");
            string output = RequirePath(ScriptBuildOutput, "腳本輸出資料夾");
            PathHistory.RememberInputPath(input);
            PathHistory.RememberOutputDirectory(output);

            return () =>
            {
                List<string> files = GetScriptTextFiles(input, srp);
                if (files.Count == 0)
                    throw new InvalidDataException("找不到可辨識的 " + (srp ? "SRP" : "VAR") + " 文字檔。");

                bool folderMode = Directory.Exists(input);
                List<Tuple<string, byte[]>> pending = new List<Tuple<string, byte[]>>();
                HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string file in files)
                {
                    string relative = folderMode ? BinaryUtil.GetRelativePathCompat(input, file) : Path.GetFileName(file);
                    string resourceExtension = srp ? ".srp" : ".var";
                    string outputRelative = GetCompiledResourcePath(relative, resourceExtension);
                    if (!destinations.Add(outputRelative))
                        throw new InvalidDataException("不同 TXT 來源會產生相同二進位輸出路徑：" + outputRelative);

                    string text = ReadScriptTextForCompile(file);
                    byte[] compiled = srp ? ScriptTextCodec.CompileSrp(text) : ScriptTextCodec.CompileVar(text);
                    pending.Add(Tuple.Create(outputRelative, compiled));
                }

                Directory.CreateDirectory(output);
                foreach (Tuple<string, byte[]> item in pending)
                {
                    string destination = Path.Combine(output, item.Item1);
                    BinaryUtil.WriteAtomically(destination, item.Item2);
                    LogLine("TXT → " + (srp ? "SRP：" : "VAR：") + item.Item1);
                }
                LogLine("回封完成：" + pending.Count + " 個檔案。");
            };
        }

        private static string ReadScriptTextForCompile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            byte[] content = offset == 0 ? bytes : bytes.Skip(offset).ToArray();
            try
            {
                return new UTF8Encoding(false, true).GetString(content);
            }
            catch (DecoderFallbackException)
            {
                // 舊版 croCKer TXT 可能使用 CP932；新檔仍固定使用 UTF-8。
                return BinaryUtil.ShiftJis.GetString(content);
            }
        }
    }
}
