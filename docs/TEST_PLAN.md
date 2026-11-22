# 自動與手動回歸測試

## GitHub Actions 自動測試

Workflow 會建置應用程式，接著執行 `tests/TmrHiroStudio.Tests`。任一測試失敗，Artifact 不會上傳。

目前自動測試涵蓋：

- 固定 `test/00_01.srp`：323 筆指令、SRP TXT v2 匯出及完整往返。
- 日文 CP932 SRP：未交換及 nibble-swapped 兩種資料狀態。
- 混有控制碼的 CP932 日文 SRP：文字仍應可讀，控制碼以 `\uXXXX` 往返。
- UTF-8 日文 SRP：編碼判斷及二進位往返。
- 無法可靠辨識的 SRP：降級為 RAW `\xHH` 並 byte-exact 往返。
- PAC SRP nibble swap：同一轉換執行兩次必須還原。
- Type 2／3：不能被當成文字，且 bytes 必須完整往返。
- CP932／UTF-8 日文 VAR：資料、長度及 `[END]`。
- VAR 第一筆空字串：匯出／回封不得遺失空紀錄。
- 一般漢字與 EUDC：一般漢字不得被反向錯轉成 EUDC；映射字元以明確 `\g{}` token 回寫，未映射字元保留 hex。
- 原子寫入：預設拒絕覆蓋，並確認原檔 bytes 不變。
- GRD：PNG → GRD → PNG 的 BGRA／Alpha 逐 byte 一致，且預設拒絕覆蓋。

這些測試不等於完整 GUI 測試，也不涵蓋所有不同遊戲版本或所有 GRD 壓縮樣本。

## 手動實機測試

### PAC

- [ ] V1、V2 PAC 分別解包；比對資源數量、索引順序及 manifest。
- [ ] 使用日文／全形檔名，原樣回封後由遊戲載入。
- [ ] PAC 原樣回封：比較未修改資源 bytes；SRP 需考慮 PAC 儲存層轉換。
- [ ] 修改一個 GRD、一個 SRP，刪除一個資源，再回封並實際載入。
- [ ] 多 PAC 批次解包遇到壞檔時，確認輸出目錄沒有留下半套已提交結果。
- [ ] 將輸出目錄指定為既有資料夾，確認既有檔案不會被覆蓋。
- [ ] 測試截斷索引、錯誤 data_offset、負數 V2 offset、越界資源範圍及重複名稱。

### GRD

- [ ] RLE 24-bit、32-bit 有 Alpha、32-bit 無 Alpha 解碼。
- [ ] Huffman + RLE（`0xA1`）、Huffman + LZ77（`0xA2`）解碼。
- [ ] 修改 PNG 像素及透明度，回封後逐像素檢查。
- [ ] 修改 PNG 尺寸，確認 metadata 座標及畫布尺寸。
- [ ] 24bpp 來源 PNG 新增透明像素時，確認程式拒絕靜默丟棄 Alpha。
- [ ] 批次中放入一個損壞 GRD／PNG，確認目的輸出目錄不會留下部分已提交的批次。

### SRP／VAR

- [ ] 原始日文遊戲 SRP 直接匯出，檢查 TXT `Encoding` 及 `Transform`，確認日文假名／漢字可讀。
- [ ] 從 PAC 解包的日文 SRP 匯出，與直接 SRP 比較其 `Transform` 判斷可能不同。
- [ ] 同一檔案 TXT → SRP 往返後逐指令比較 Data、Type、Flags、footer。
- [ ] 確認 Type 2／3、Type 80 及未知 Type 以 hex 保留。
- [ ] 日文 CP932 VAR、UTF-8 VAR 分別匯出、修改及回封。
- [ ] 以 `RAW` 標頭匯出時，確認沒有把未知 bytes 誤顯示成文字。
- [ ] 檢查 `\xHH`、`\n`、`\r`、`\t`、`\0`、`\\` 及 `\g{}` 跳脫。
- [ ] 舊 croCKer TXT 先用副本測試回編，不要直接覆蓋遊戲原檔。
- [ ] 輸出資料夾存在相同檔名時，確認停止而不是覆蓋翻譯稿。

### GUI／部署

- [ ] Windows 10 x64 啟動；PAC、GRD、SRP、VAR 各分頁逐一執行。
- [ ] 工作期間不可重複啟動其他工作；記錄區仍可操作。
- [ ] 免安裝資料夾唯讀時，確認程式啟動及路徑設定不會寫入程式目錄。
- [ ] Actions 成功後下載 Artifact；解壓一次即可看見 EXE、TSV 與文件，不應有內層 ZIP。
