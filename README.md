# TmrHiroStudio 1.0.6

TmrHiroStudio 是供 **Tmr-Hiro ADV System** 遊戲資源使用的 Windows x64 圖形工具，處理 PAC 封包、GRD 圖像，以及 SRP／VAR 腳本。

## 程式碼來源與協作

本專案部分程式碼、功能修正與技術文件由 **ChatGPT（OpenAI）** 依專案維護者提出的需求協助撰寫、檢查及修正。功能整合、需求決定、實際遊戲測試與版本維護由 Repository 維護者負責。格式相容性仍應以原始資源備份及實際遊戲測試確認。

## 功能

### PAC 封包

- 支援 PAC Version 1／2 解包與回封。
- 解包時建立 `.tmrhiro.pac.json`，記錄封包版本、原始資源名稱位元組及索引順序。
- 使用 manifest 回封時沿用原始名稱及索引順序；刪除某個解包資源檔代表不將該資源放回封包。
- SRP 資源會處理 PAC 儲存層的 nibble swap；一般資源（包含 GRD）以原始位元組寫入。
- PAC 本身不保存目錄階層；回封只讀取指定資料夾第一層的檔案。請勿把要回封的資源只放在子資料夾中。
- 回封時會略過 `.grd.json`、`.srp.txt`、`.var.txt` 及 PAC manifest；PNG 不會自動略過，請勿將 PNG 留在 PAC 回封資料夾。

### GRD 圖像

- 支援 GRD Version 1／2、24／32 bpp。
- 可解碼 PackType 1、`0xA1`、`0xA2`，輸出 PNG 及同名 `.grd.json` 中繼資料。
- PNG → GRD 會依 PNG 像素重新建立通道資料，並在寫出前執行解碼往返驗證。
- **目前 PNG → GRD 統一使用 RLE（PackType 1）重新壓縮**。原始 GRD 若是 `0xA1`／`0xA2`，回封後的壓縮格式會改為 PackType 1；Version 低位元組則沿用中繼資料。
- 24 bpp GRD 不含 Alpha 通道。若 PNG 有透明像素，而 sidecar 指定 24 bpp，工具會拒絕輸出，避免無聲丟失透明資訊。
- 沒有 `.grd.json` 時會套用新建圖像的預設 metadata（32 bpp、左上角起始）。修改既有遊戲圖像時，請保留原始 sidecar。

### SRP／VAR 腳本

- SRP 使用完整 32-bit opcode 判斷訊息、旁白、選項及其他指令。
- 選項前綴、跳轉標籤與未知二進位 Data 會保留；未知 Data 以 `\xHH` 表示。
- 新版 SRP TXT 使用 `TMRHIRO-SRP-TEXT/4`，VAR TXT 使用新版格式標頭；TXT 檔案本身以 UTF-8 讀取。
- SRP／VAR Data 可選 CP932（Shift-JIS）、GBK／CP936、UTF-8 或 RAW。編碼資訊寫在 TXT 標頭，回封時依標頭處理。
- AUTO 僅供參考。CP932 與 GBK 有共用字元碼位，不能保證自動判斷正確；重要檔案請明確選擇編碼。
- 支援批次匯出 SRP + VAR，以及先完整解析再提交的 TXT → SRP + VAR 批次回封。

## 修圖流程：PAC → GRD → PNG → GRD → PAC

1. 在「PAC 封包」頁解包原始 PAC。保留輸出資料夾內的 `.tmrhiro.pac.json`。
2. 在「GRD 圖像」頁，以 PAC 解包資料夾作為 GRD 輸入，輸出 PNG。每張 PNG 旁會產生對應的 `.grd.json`。
3. 使用圖像編輯器修改 PNG。**保持 PNG 檔名不變，並將對應的 `.grd.json` 留在 PNG 同一資料夾**。只修改像素時，通常不需要編輯 JSON。
4. 執行 PNG → GRD，輸出到另一個暫存資料夾。
5. 將產生的 GRD 檔複製回 PAC 解包資料夾，覆蓋同名的原始 GRD。不要把 PNG 或 `.grd.json` 放進 PAC 資源資料夾。
6. 回到「PAC 封包」頁，選擇原本的 PAC 解包資料夾回封。保留 manifest，PAC 版本選「自動（優先使用 manifest）」。
7. 將新 PAC 放在遊戲資源的備份／測試位置，確認遊戲顯示正常後再替換正式檔案。

不要直接把「PNG 輸出資料夾」或「GRD 輸出資料夾」當成 PAC 回封資料夾；PAC 回封應使用原本含有 `.tmrhiro.pac.json` 的解包資料夾，並把修好的同名 GRD 放回其中。

## GRD JSON 欄位說明

範例：

```json
{"Format":257,"ScreenWidth":1280,"ScreenHeight":720,"Bpp":24,"Left":0,"Right":1280,"Top":0,"Bottom":720,"AlphaSize":0,"RSize":818984,"GSize":823546,"BSize":825038}
