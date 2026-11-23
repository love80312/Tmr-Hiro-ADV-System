# TmrHiroStudio 1.0.5 TXT 回封修正

此套檔案是以目前 `main` 分支 1.0.4 原始碼為基礎的覆蓋／新增檔案，不會變更 PAC、GRD、SRP 二進位解析器或 nibble-swap 演算法。

## 覆蓋檔案

1. `src/TmrHiroStudio/MainWindow.xaml`
2. `src/TmrHiroStudio/TmrHiroStudio.csproj`
3. `tests/TmrHiroStudio.Tests/Program.cs`

## 新增檔案

1. `src/TmrHiroStudio/Core/ScriptTextFileCodec.cs`
2. `src/TmrHiroStudio/MainWindow.ScriptBuildFix.cs`

新 `.cs` 檔會由 SDK-style 專案自動納入，不需要修改 `.csproj` 的 Compile 清單。

## 修正內容

- 修正 TXT v4 標頭漏列造成的編碼誤判：v4 TXT 檔案本身以 UTF-8 讀取，`Encoding: GBK` 只控制 SRP Data 重新編碼。這是原本 `0x87 0x0A` CP932 解碼錯誤的根因。
- 接受 UTF-8 BOM 與 UTF-16 BOM；無標頭的 croCKer 舊 TXT 仍採嚴格 CP932。
- 更新標準 `TXT → SRP`、`TXT → VAR` 按鈕使用修正後的讀檔器。
- 新增 `TXT → SRP + VAR`：同一輸入資料夾中的 `.srp.txt` 與 `.var.txt` 一起編譯；兩類都必須找到至少一個檔案；全部解析完成才提交，避免只產生一半。
- 批次輸出仍使用原本的 no-overwrite staging 機制，不會覆蓋輸出資料夾內既有檔案。
- 將版本標示更新為 1.0.5。
- 新增兩項回歸測試，檢查 SRP TXT v4 UTF-8 + GBK payload，以及 VAR TXT v3 UTF-8。

## CN2 原樣回封測試流程

1. 用 1.0.5 開啟原本可用的 CN2 `cn.pac`，解包到新資料夾，保留 `.tmrhiro.pac.json`。
2. 將原本的 CN2 GBK 劇本 TXT（`*.srp.txt`）與 VAR TXT（`*.var.txt`）放在同一個輸入資料夾，保留檔名及子資料夾結構。
3. 在「TXT 檔案／資料夾」選擇上述資料夾；「二進位輸出資料夾」選擇一個全新的空資料夾。
4. 按 `TXT → SRP + VAR`。輸出應同時包含 `.srp` 與 `.var`。
5. 將生成的 `.srp`／`.var` 檔案複製到第 1 步的 PAC 解包資料夾，依原檔名覆蓋對應資源。**只替換腳本資源，不要刪除 manifest 或其他 PAC 資源。**
6. 使用 PAC 頁籤的回封功能，選擇第 1 步的解包資料夾；PAC 版本選「自動（優先使用 manifest）」；輸出成新的測試 PAC。
7. 備份原始 `cn.pac`，再用新 PAC 搭配 `CN.exe` 測試。先測試進入遊戲、劇情文字、選項、場景切換，再測存檔／讀檔。

## 注意

- 本次修正能修復 TXT v4 的讀取路徑；不能單憑建置成功保證 CN.exe 一定接受重建後的 PAC。
- 「原樣」指 TXT 沒有做文字修改。因 SRP 重新序列化會重建每筆 Length，仍應先以原樣回封測試 CN.exe；不要直接覆蓋唯一原始 PAC。
- 本環境沒有 .NET Framework 4.8／Windows WPF 建置環境，因此尚未在此執行 Windows 編譯。請以 GitHub Actions 建置及測試結果作為編譯驗證。
