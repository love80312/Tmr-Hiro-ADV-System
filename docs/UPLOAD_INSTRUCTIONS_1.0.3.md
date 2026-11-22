# 1.0.3 修正檔案包上傳方式

本包是 Repository 的覆蓋／新增檔案包，不是完整 Repository 備份。請保留既有、未列入本包的程式檔案。

## 上傳

1. 下載 `TmrHiroStudio_1.0.3_CompleteFix_Overlay.zip` 並解壓縮。
2. 將解壓縮後各檔案依相對路徑上傳至 `love80312/Tmr-Hiro-ADV-System` 的 `main` 分支根目錄。
3. 同名檔案選擇覆蓋；不存在的檔案新增。不要把最外層包裝資料夾整個上傳成 Repository 子目錄。
4. 至少需要更新 `.github/workflows/build.yml`、`README.md`、`TmrHiroStudio.sln`、`docs/FORMAT_NOTES.md`、`docs/TEST_PLAN.md`、`docs/ENCODING_AND_SRP_FIXES_1.0.3.md`、`docs/UPLOAD_INSTRUCTIONS_1.0.3.md`、`src/TmrHiroStudio` 中修正的完整檔案，以及新增的 `tests/TmrHiroStudio.Tests`。
5. 保留原有 `src/TmrHiroStudio/MainWindow.xaml.cs`、`App.xaml`、`App.xaml.cs`、`Models`、`NativeFolderPicker.cs`、`PathHistory.cs`、`LICENSE`、`THIRD_PARTY_NOTICES.md` 和其他未修改檔案。
6. 不需要上傳不存在的 `test/00_01.srp`；本版自動測試採合成資料，不依賴該檔案。原本 `test/` 中的 CN.exe、JP.exe 與 PAC 樣本可保留，不會成為建置必要條件。

## 執行測試及取得程式

1. 上傳完成後，開啟 GitHub Actions。
2. 執行 **Build TmrHiroStudio**。
3. Workflow 會 restore、建置 Release x64 主程式及測試專案，並執行 17 項合成回歸測試。
4. 全部成功後，下載 `TmrHiroStudio-Win10-x64` Artifact。

本次交付包已完成原始碼靜態檢查與檔案結構檢查；提供檔案的執行環境沒有 .NET Framework 4.8／WPF 建置工具，因此沒有宣稱已在此環境成功編譯或執行這 17 項 C# 測試。請以你上傳後的 Actions 結果作為實際建置及自動測試結果。
