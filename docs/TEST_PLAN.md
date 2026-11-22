# 手動回歸測試清單

目前專案不附帶商業遊戲資源或二進位測試樣本。第一次建置後，請用自己合法持有的遊戲檔案副本執行下列測試。

## PAC

- [ ] V1 PAC 解包：檢查檔案數量、索引順序及 `.tmrhiro.pac.json` 的 `FormatVersion`。
- [ ] V2 PAC 解包：確認 manifest 記錄 Version 2。
- [ ] 使用含日文／全形字元的資源名稱測試：確認解包檔名可讀，回封後索引名稱仍可被遊戲辨識。
- [ ] 原樣回封：比較解包前後的資源數、資源 bytes（SRP 以 PAC 層轉換還原後比較），並在遊戲中載入測試。
- [ ] 修改一個 `.grd`、一個 `.srp`，刪除一個資源，再回封；確認刪除的資源不再存在於新 PAC。
- [ ] 將輸出路徑指定到輸入資料夾內，確認新建 PAC 不會被當成下一次輸入資源。
- [ ] 使用截斷標頭、錯誤 data_offset、負數 V2 offset 或超出檔案範圍的 PAC，確認程式拒絕解包且不留下半成品輸出目錄。

## GRD

- [ ] 24-bit RLE GRD → PNG → GRD。
- [ ] 32-bit、含 Alpha 的 RLE GRD → PNG → GRD。
- [ ] 32-bit、無 Alpha 的 GRD → PNG → GRD。
- [ ] Huffman + RLE（`0xA1`）及 Huffman + LZ77（`0xA2`）解碼。
- [ ] 對輸出的 PNG 改一個像素，回封後確認畫面顏色與 Alpha 正確。
- [ ] 修改 PNG 尺寸，確認 metadata 的圖像座標及畫布大小合法更新。
- [ ] 使用你提供的 `sys_window_menu.grd` 作為回歸樣本：確認輸出尺寸為 1280 × 48，原始版本低位元組為 2，半透明 Alpha 外觀正常；輸出 GRD 的壓縮類型可變為 RLE，但版本低位元組仍應保留 2。
- [ ] 對截斷標頭、通道長度不符、非法 RLE／Huffman／LZ77 stream，確認程式拒絕處理。

## SRP／VAR

- [ ] SRP 二進位 → TXT → SRP：二進位資料應可逐指令還原；count 欄位以重新編碼的實際指令數為準。
- [ ] 舊 `croCKer` Shift-JIS SRP TXT 資料夾批次回編。
- [ ] 新標頭 UTF-8 SRP TXT 批次回編。
- [ ] VAR 二進位 → TXT → VAR：檢查每筆字串與最終 `[END]`。
- [ ] 舊 `croCKer` VAR TXT（UTF-8、結尾 `[END]`）批次回編。
- [ ] 測試資料中的反斜線、換行、Tab、NUL 及 `\xHH` 跳脫。
- [ ] 移除 SRP 結尾指令、移除 VAR `[END]`、截斷長度欄位，確認程式拒絕回編。

## 發布包

- [ ] GitHub Actions 成功完成 Release x64 build。
- [ ] Artifact ZIP 內含 `TmrHiroStudio.exe`、`TmrHiroStudio.exe.config`（若有產生）、README、LICENSE 及 notices。
- [ ] Windows 10 x64 上直接執行，不需額外安裝 .NET Desktop Runtime。
