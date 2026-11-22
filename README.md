# TmrHiroStudio 1.0.3 Hotfix 01

本包只修正 1.0.3 在 GitHub Actions run 45015376776 中暴露的 SRP NIBBLE_SWAP 雙重轉換問題。

## 覆蓋檔案

- `src/TmrHiroStudio/Core/ScriptTextCodec.cs`
- `tests/TmrHiroStudio.Tests/Program.cs`

請依照原相對路徑覆蓋 Repository 同名檔案。其餘檔案不必變動。

詳細原因及驗證方式請見 `docs/HOTFIX_1.0.3_01.md`、`docs/UPLOAD_INSTRUCTIONS_HOTFIX_1.0.3_01.md`。
