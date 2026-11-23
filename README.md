# TmrHiroStudio 1.0.4 — SRP opcode／文字解碼修正

本修正版重新整理 SRP 指令識別與文字匯出方式。請依照 ZIP 內的相對路徑，覆蓋 GitHub Repository 同名檔案。ZIP 包含完整可覆蓋檔案及新增文件；沒有需要刪除的專案檔案。

## 本次修正重點

- SRP 指令使用完整 32-bit opcode 判斷，不再只看 Type。
- 正確辨識 `0x00150050` 訊息、低 16 位為 `0` 的旁白／文字，以及 `0x00140010` 選項。
- 選項前 3 bytes 以 `\xHH` 保留，後續選項字串可按指定編碼閱讀與修改；跳轉標籤保持原文。
- SRP 依每筆 length 與檔案邊界解析至 EOF，不再強制要求 Type 16／Flags 3 作為唯一 footer。
- PAC 解包已還原 Data nibble swap；SRP TXT 匯出不再提供容易造成第二次交換的 Transform 選項。
- 新 TXT 格式為 `TMRHIRO-SRP-TEXT/4`。舊 v1／v2／v3 SRP TXT 仍保留相容解析。
- 未知 opcode 與非文字 Data 一律以 `\xHH` 保存，避免破壞二進位參數。
- VAR 格式保留原有讀寫行為。
- 新增 18 項回歸測試，涵蓋提供的 GBK bytes、兩種上游 opcode、選項前綴、PAC 轉換與舊 TXT 相容。

## 覆蓋檔案

共覆蓋 11 個既有檔案：

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

另新增 3 份文件：`docs/SRP_OPCODE_FIX_1.0.4.md`、`docs/REFERENCE_REVIEW_SRP_UPDATE_1.0.4.md`、`docs/UPLOAD_INSTRUCTIONS_SRP_OPCODE_FIX.md`。詳細操作順序與保留清單請看最後一份文件。

## 使用方式

1. 對 `cn.pac`、`jp.pac` 分別執行 PAC 解包。
2. SRP → TXT 時，簡體中文內容選 GBK／CP936；日文內容選 CP932。
3. 編輯 TXT 時保留 v4 標頭、Type、Flags，以及選項前綴與跳轉標籤。
4. TXT → SRP 後再將 SRP 放回 PAC 回封資料夾，最後執行 PAC 回封。

`AUTO` 只能按文字特徵估計編碼。CP932 與 GBK 共用部分漢字碼位，不能保證正確；重要資料建議明確選擇編碼。

## 驗證限制

測試專案加入了與上游 opcode 定義一致的合成案例，以及你提供的三組 GBK 原始 bytes。這個工作環境沒有 .NET Framework 4.8／MSBuild 編譯工具，因此此 ZIP 尚未在此環境實際執行 Windows WPF 編譯或測試。請以 GitHub Actions 的 build／test 結果作最後確認；本次也沒有聲稱已完成整份 `cn.pac`、`jp.pac` 的逐筆實檔測試。
