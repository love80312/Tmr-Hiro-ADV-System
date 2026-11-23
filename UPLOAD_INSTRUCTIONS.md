# GitHub 網頁版維護與建置流程（1.0.6）

本文件適用於透過 GitHub 網頁介面維護 `love80312/Tmr-Hiro-ADV-System` 的流程。它取代舊版一次性修正包的上傳說明；舊版指引只保留作為歷史紀錄，請勿照舊文件中的舊檔案清單或測試數量操作。

## 上傳 Repository 檔案

1. 下載整理包並解壓縮。
2. 依照 ZIP 內的相對路徑，在 GitHub `main` 分支覆蓋同名檔案或新增新檔案。不要把最外層資料夾名稱當成 Repository 子資料夾。
3. 本次整理只更新 README、第三方參考聲明、文件索引、測試計畫、此維護說明及 Actions Workflow；不替換 PAC／GRD／SRP／VAR 核心程式碼，也不要求刪除 `test/` 樣本。
4. 提交到 `main` 後，`push` 事件會自動執行 **Build TmrHiroStudio**。

## Actions 執行內容

Workflow 會依序：

1. 還原 WPF 主程式與測試專案。
2. 建置 Release x64 主程式。
3. 建置回歸測試專案。
4. 執行 20 項 SRP／VAR 格式回歸測試。
5. 建立 `TmrHiroStudio-Win10-x64` 免安裝資料夾並上傳 Artifact。

只有建置與測試全部成功，才會執行 Artifact 上傳。Artifact 目前保留 30 天；請在期限內下載保存。

## 下載免安裝程式

1. 開啟 Repository 的 **Actions**。
2. 選擇最新成功的 **Build TmrHiroStudio** 執行紀錄。
3. 在頁面下方的 Artifacts 區塊下載 `TmrHiroStudio-Win10-x64`。
4. 解壓縮後執行 `TmrHiroStudio.exe`。請保留同資料夾內的 `cp932-pua-map.tsv`、`TmrHiroStudio.exe.config`（若有）、`LICENSE`、`README.md` 及 `THIRD_PARTY_NOTICES.md`。

## 最近一次已驗證執行

- [Actions Run #26](https://github.com/love80312/Tmr-Hiro-ADV-System/actions/runs/45211092068)
- Commit：`952f28cb2796bf1d55b35e8bf24ac65bc45813d8`
- 結果：主程式及測試專案建置成功，20 項回歸測試全部通過，Artifact 上傳成功。

後續每次提交仍應以該次 Actions 結果為準，不要把舊 Run 的成功視為新提交已通過。

## 測試範圍提醒

20 項自動測試主要驗證 SRP／VAR 解析、編碼、未知 bytes 保留及 PAC 層 nibble swap。它們不取代完整 PAC／GRD 樣本檢查及遊戲實機測試。測試範圍與手動清單請見 [`docs/TEST_PLAN.md`](docs/TEST_PLAN.md)。
