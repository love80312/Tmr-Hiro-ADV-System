# 文件索引

本資料夾保存格式規格、測試計畫、參考專案檢視及各版本修正紀錄。**目前操作方式與功能限制以 Repository 根目錄的 `README.md` 為準；目前自動測試範圍以 `TEST_PLAN.md` 為準。**

## 現行維護文件

| 文件 | 說明 |
|---|---|
| [`FORMAT_NOTES.md`](FORMAT_NOTES.md) | PAC、SRP、VAR 格式結構、opcode、TXT 標頭與回封規則 |
| [`TEST_PLAN.md`](TEST_PLAN.md) | 目前 CI 自動測試項目、手動實機測試清單及已知驗證範圍 |
| [`REFERENCE_REVIEW.md`](REFERENCE_REVIEW.md) | 主要參考專案的程式碼檢視與獨立實作決策 |
| [`REFERENCE_REVIEW_SRP_UPDATE_1.0.4.md`](REFERENCE_REVIEW_SRP_UPDATE_1.0.4.md) | SRP opcode、PAC nibble swap 及來源比對補充 |
| [`ENCODING_AND_SRP_FIXES_1.0.3.md`](ENCODING_AND_SRP_FIXES_1.0.3.md) | 1.0.3 編碼策略與後續修正背景；現行規則以 `FORMAT_NOTES.md` 為準 |

第三方參考來源、參考範圍及授權狀態集中記錄於 Repository 根目錄的 [`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md)。

## 版本修正紀錄

以下文件保留當時的問題、修正內容與背景，**屬歷史紀錄，不代表目前程式仍採用該版本的實作方式**：

- [`FIXES_1.0.1.md`](FIXES_1.0.1.md) — 1.0.1 WPF 執行緒、路徑記憶及資料夾選擇器修正。
- [`REPAIR_NOTES_1.0.1.md`](REPAIR_NOTES_1.0.1.md) — 1.0.1 舊 SRP／VAR 假設的修正背景。
- [`FIXES_1.0.2.md`](FIXES_1.0.2.md) — 編碼、PUA、批次輸出及早期回歸測試修正。
- [`ENCODING_AND_SRP_FIXES_1.0.3.md`](ENCODING_AND_SRP_FIXES_1.0.3.md) — 1.0.3 編碼策略與 1.0.4 後續修正。
- [`HOTFIX_1.0.3_01.md`](HOTFIX_1.0.3_01.md) — 1.0.3 Hotfix 的 nibble swap 雙重處理問題。
- [`SRP_OPCODE_FIX_1.0.4.md`](SRP_OPCODE_FIX_1.0.4.md) — 1.0.4 SRP opcode 分類與解析原則更新。

## 舊版上傳指引（僅供追溯）

以下文件只對應當時的覆蓋包、舊版檔案清單或舊測試流程。**不要用它們判斷目前版本需要哪些檔案，也不要依照其中已過時的測試數量或外部測試檔案需求操作。**

- [`UPLOAD_INSTRUCTIONS_1.0.2.md`](UPLOAD_INSTRUCTIONS_1.0.2.md)
- [`UPLOAD_INSTRUCTIONS_1.0.3.md`](UPLOAD_INSTRUCTIONS_1.0.3.md)
- [`UPLOAD_INSTRUCTIONS_HOTFIX_1.0.3_01.md`](UPLOAD_INSTRUCTIONS_HOTFIX_1.0.3_01.md)
- [`UPLOAD_INSTRUCTIONS_SRP_OPCODE_FIX.md`](UPLOAD_INSTRUCTIONS_SRP_OPCODE_FIX.md)

目前的 GitHub 網頁版維護及 Actions 取件流程請見 Repository 根目錄的 [`UPLOAD_INSTRUCTIONS.md`](../UPLOAD_INSTRUCTIONS.md)。
