# 第三方參考專案與授權聲明

TmrHiroStudio 是獨立實作。本專案在研究 PAC、GRD、SRP／VAR 格式及比對相容行為時，參考下列公開專案的文件、格式說明或可觀察行為。依目前的程式碼檢視紀錄，沒有直接複製這些專案的原始碼。

| 專案 | 參考範圍 | 檢視時的授權資訊 |
|---|---|---|
| [Chenx221/Tmr-Hiro_PAC_Repack](https://github.com/Chenx221/Tmr-Hiro_PAC_Repack) | PAC V1／V2 索引結構、Shift-JIS 名稱、SRP nibble transformation | Repository 內有 MIT License |
| [HOKORISAMA/Tmr-hiro-grd-tools](https://github.com/HOKORISAMA/Tmr-hiro-grd-tools) | PAC 解包行為、GRD 通道解碼 | 檢視時未在 Repository 根目錄找到授權檔；未複製原始碼 |
| [crskycode/TmrHiro_Tool](https://github.com/crskycode/TmrHiro_Tool) | GRD metadata 與 RLE encoder 作法 | 檢視時未在 Repository 根目錄找到授權檔；未複製原始碼 |
| [TheVNConnoisseur/croCKer](https://github.com/TheVNConnoisseur/croCKer) | 早期 SRP／VAR 文字結構與轉換行為 | Repository 內有 GNU GPL v3.0；僅作格式參考，未複製原始碼 |
| [rafael-vasconcellos/VNTextPatch-net8](https://github.com/rafael-vasconcellos/VNTextPatch-net8) | Tmr-Hiro CodeScript 訊息／選項 opcode 與欄位邊界 | MIT License；僅作格式參考，未複製原始碼 |
| [satan53x/SExtractor](https://github.com/satan53x/SExtractor) | Tmr-Hiro opcode 比對及選項前綴處理 | GPL-3.0；僅作格式參考，未複製原始碼 |
| [nanami5270/TmrHiro_Tool](https://github.com/nanami5270/TmrHiro_Tool) | 與 crskycode/TmrHiro_Tool 相同的 GrdTool source tree | 檢視時未在 Repository 根目錄找到授權檔；此來源與 crskycode 項目內容相同，不視為另一套獨立實作 |
| [TheVNConnoisseur/PACkager](https://github.com/TheVNConnoisseur/PACkager) | PAC V1／V2 pack／unpack 行為及 PAC 層 SRP transformation | Repository 內有 GNU GPL v3.0；僅作格式參考，未複製原始碼 |
| [GARbro](https://github.com/morkt/GARbro) | Tmr-Hiro PAC／GRD 格式辨識及 decoder 行為 | MIT License；僅作格式研究參考，未複製原始碼 |

更完整的檢視內容與整合決策請參閱 [`docs/REFERENCE_REVIEW.md`](docs/REFERENCE_REVIEW.md) 及 [`docs/REFERENCE_REVIEW_SRP_UPDATE_1.0.4.md`](docs/REFERENCE_REVIEW_SRP_UPDATE_1.0.4.md)。

本文件記錄的是檢視當時可見的授權狀態，不構成法律意見，也不代表取得任何第三方程式碼的再散布授權。若未來直接複製或改寫其他專案的程式碼，必須先重新確認該專案的授權條款，並依其要求更新本文件、原始碼聲明及本專案的授權安排。
