# 1.0.2 完整修正包上傳方式

這個 ZIP 是**覆蓋／新增檔案包**，不是完整 Repository 備份。請解壓縮後，將其中的目錄及檔案依照原相對路徑上傳到 `love80312/Tmr-Hiro-ADV-System` 的 `main` 分支根目錄。

## 操作方式

1. 下載並解壓縮 `TmrHiroStudio_1.0.2_CompleteFix_Overlay.zip`。
2. 進入解壓後的 `TmrHiroStudio_CompleteFix` 資料夾。
3. 將 `.github`、`docs`、`src`、`tests` 目錄及 `README.md` 依原路徑上傳至 Repository 根目錄。
4. 同名檔案選擇覆蓋；不存在的檔案則新增。不要把 `TmrHiroStudio_CompleteFix` 外層資料夾整個再包成 Repository 子資料夾。
5. 原 Repository 中的 `test/00_01.srp` 必須保留，因為新測試專案會用它做固定回歸測試。
6. 不需要刪除 `src/TmrHiroStudio/MainWindow.xaml.cs`、`Models/Metadata.cs`、`App.xaml`、`NativeFolderPicker.cs`、`PathHistory.cs`、`LICENSE` 或 `THIRD_PARTY_NOTICES.md`；這些仍沿用原 Repository 的檔案。
7. 上傳後開啟 Actions，執行 **Build TmrHiroStudio**。新的 Workflow 會先建置應用程式及測試程式，再執行格式回歸測試；任一測試失敗就不會上傳 Artifact。

## 本包檔案

本包包含需要覆蓋／新增的完整檔案，合計 22 個。新增的測試程式只會在 Actions 的測試輸出目錄建立合成 PUA 對照表，不會修改正式附帶的 `src/TmrHiroStudio/cp932-pua-map.tsv`。正式對照表仍是空白範本，沒有加入未經遊戲字型或字碼資料證實的字形。

## 測試限制

本修正包已做原始碼靜態檢查及測試案例設計，但目前提供檔案的環境沒有 .NET Framework 4.8／WPF 編譯執行環境，因此沒有宣稱已在本機完成 Windows 編譯或實機測試。請以你上傳後的 GitHub Actions 結果確認建置與自動測試，再用遊戲副本測試實際載入。
