# Hotfix 01 上傳方式

1. 解壓縮本包。
2. 在 GitHub Repository 的 `main` 分支，依原路徑覆蓋以下兩個完整檔案：
   - `src/TmrHiroStudio/Core/ScriptTextCodec.cs`
   - `tests/TmrHiroStudio.Tests/Program.cs`
3. 不需要刪除或覆蓋其他檔案；尤其不要用舊版整包覆蓋目前 Repository，以免回退你後續提交的修改。
4. 上傳後開啟 Actions，確認 `Build Release x64`、`Build regression tests`、`Run format regression tests` 及 `Prepare portable package` 均成功。

此 Hotfix 不修改 GitHub Actions Workflow，也不要求重新上傳測試用遊戲檔案。
