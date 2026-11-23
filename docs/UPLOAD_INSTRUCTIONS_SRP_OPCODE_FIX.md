# GitHub 網頁操作上傳說明

## 覆蓋檔案

請依照 ZIP 內的相對路徑覆蓋以下 11 個檔案：

```text
README.md
THIRD_PARTY_NOTICES.md
src/TmrHiroStudio/Core/ScriptCodec.cs
src/TmrHiroStudio/Core/ScriptTextCodec.cs
src/TmrHiroStudio/MainWindow.xaml
src/TmrHiroStudio/MainWindow.ScriptEncodingSettings.cs
src/TmrHiroStudio/TmrHiroStudio.csproj
tests/TmrHiroStudio.Tests/Program.cs
docs/FORMAT_NOTES.md
docs/ENCODING_AND_SRP_FIXES_1.0.3.md
docs/TEST_PLAN.md
```

## 新增文件

```text
docs/SRP_OPCODE_FIX_1.0.4.md
docs/REFERENCE_REVIEW_SRP_UPDATE_1.0.4.md
docs/UPLOAD_INSTRUCTIONS_SRP_OPCODE_FIX.md
```

ZIP 根目錄的 `README.md` 也用來更新 Repository 首頁說明，避免首頁仍只記載 1.0.3 Hotfix。

## 建議上傳順序

1. 先覆蓋兩個 Core 檔案。
2. 再覆蓋 XAML、設定 code-behind 與 csproj。
3. 覆蓋測試與格式文件。
4. 新增三份文件。
5. 提交後執行 GitHub Actions。

## 測試重點

- `Type 0／Flags 0` 的 GBK 旁白文字。
- `Type 80／Flags 21` 的 `0x00150050` 訊息。
- `Type 16／Flags 20` 的 `0x00140010` 選項。
- 選項最前方 3 bytes 與跳轉標籤保留。
- 無 Type 16／Flags 3 footer 的 SRP。
- 原始 HeaderCount 與實際指令數不一致的 SRP。
- PAC 解包／回封各一次 nibble swap，資料可逆。
- GBK／CP932／UTF-8／RAW VAR 往返。

## 不要刪除的檔案

以下檔案仍屬專案必要資料或參考資料：

- `src/TmrHiroStudio/Core/PacCodec.cs`
- `src/TmrHiroStudio/Core/GrdCodec.cs`
- `src/TmrHiroStudio/Core/BinaryUtil.cs`
- `src/TmrHiroStudio/Core/PuaCharacterMap.cs`
- `src/TmrHiroStudio/cp932-pua-map.tsv`
- `src/TmrHiroStudio/Models/Metadata.cs`
- `src/TmrHiroStudio/PathHistory.cs`
- `src/TmrHiroStudio/NativeFolderPicker.cs`
- `.github/workflows/build.yml`
- `test/cn.pac`、`test/jp.pac` 與對應 EXE 測試資料
- 舊版修正紀錄與第三方授權文件

舊文件中若仍記載「Type 0／1 是文字」、「必須有 Type 16／Flags 3 footer」或「SRP 預設需要第二層 Transform」，應以新版 `FORMAT_NOTES.md` 為準；歷史版本文件不必刪除。
