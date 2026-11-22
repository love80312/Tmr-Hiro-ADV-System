# TmrHiroStudio 1.0.1 修正說明

## 1. 修正 WPF 跨執行緒存取

舊版將整個事件委派直接放入 `Task.Run`，事件委派內又讀取 `TextBox.Text`，因此 WPF 在背景執行緒擲出：

> 呼叫執行緒無法存取此物件，因為此物件屬於另一個執行緒。

新版 `RunOperation` 改為接收 `Func<Action>`：

1. `prepareOperation` 在 UI 執行緒執行。
2. 在 UI 執行緒讀取及驗證路徑、版本選項。
3. 回傳的 `Action` 只包含已擷取的路徑、數值與核心方法。
4. 僅將此 `Action` 傳入 `Task.Run`。

`LogLine` 保留 Dispatcher 派送，供背景工作安全寫入執行記錄。

## 2. 路徑記憶

新增 `PathHistory.cs`，將以下資料存放至：

`%LocalAppData%\\TmrHiroStudio\\settings.json`

- 上一次選擇檔案的資料夾。
- 上一次選擇資料夾的位置。

選擇器的起始位置依序為：

1. 目前輸入欄位能解析出的現有資料夾或最近存在的父資料夾。
2. 相同類型上次使用的位置。
3. 另一類型上次使用的位置。
4. 程式所在目錄。

設定檔損壞或無法寫入時，會使用預設路徑，不會阻止主要功能執行。

## 3. Windows 原生資料夾選擇器

新增 `NativeFolderPicker.cs`，透過 Windows Shell COM `IFileDialog` 與 `FOS_PICKFOLDERS` 顯示 Explorer 風格資料夾選擇視窗。

- 支援路徑列、磁碟、導覽窗格及建立新資料夾。
- 取消選擇不會更動原本輸入欄位。
- 對話框關閉後釋放 COM 物件。
- 不再使用 WinForms `FolderBrowserDialog`。

## 4. 工作期間的 GUI

- 工作期間停用功能分頁，防止重複啟動。
- 執行記錄仍可捲動及清除。
- 狀態列顯示目前工作名稱。
- 顯示不定進度指示，避免使用者誤以為程式沒有反應。

## 5. 相容性

- 保留 .NET Framework 4.8、WPF、x64。
- 移除 `UseWindowsForms`，不再為資料夾選擇器引用 WinForms。
- 免安裝輸出預設放在程式目錄下 `Output` 子目錄；實際工作開始時才建立。
- 路徑設定寫入 LocalApplicationData，不寫入程式目錄。

## 6. 建議回歸測試

完整清單請見 `TEST_PLAN.md` 的「GUI／路徑回歸測試（1.0.1）」章節。請特別測試每一個操作分頁，確認不再出現跨執行緒例外，並在 Windows 10 x64 上測試原生資料夾選擇器。

本版完成原始碼與靜態結構檢查；未在此環境實際啟動 Windows WPF，也未宣稱已完成 Windows 編譯。首次建置請使用 GitHub Actions 驗證。
