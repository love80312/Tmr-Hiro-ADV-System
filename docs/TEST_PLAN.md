# 自動與手動回歸測試（1.0.3）

## 自動測試

GitHub Actions 會先建置 WPF 主程式及 `tests/TmrHiroStudio.Tests`，再執行測試程式。任一階段失敗時，不會上傳免安裝 Artifact。測試資料由程式即時建立，不依賴外部 `test/00_01.srp` 樣本。

自動測試共 17 項：

1. GBK／CP936 SRP 手動編碼及 byte-exact 往返。
2. GBK／CP936 SRP 搭配獨立 `NIBBLE_SWAP` 的往返。
3. CP932 SRP 手動編碼往返。
4. UTF-8 SRP 手動編碼往返。
5. RAW SRP 對未知位元組的逐 byte 保留。
6. 無效 GBK 位元組以 `\xHH` 保留，不使用替代字元。
7. GBK／CP936 私用造字 bytes 以 `\xHH` 保留，不當成通用 Unicode 字形。
8. SRP 原始 `HeaderCount` 在未改變指令筆數時保留。
9. 新增／刪除指令後更新 `HeaderCount`。
10. 舊版 SRP TXT v2 相容回封。
11. GBK／CP936 VAR 手動編碼往返。
12. CP932 VAR 往返。
13. UTF-8 VAR 往返。
14. RAW VAR 往返。
15. 舊版 VAR TXT v2 相容回封。
16. PAC SRP 必須通過完整指令結構及 footer 驗證，僅有開頭標記的截斷資料不得被識別為 SRP。
17. PAC 層 nibble swap 的可逆性。

以上測試驗證合成資料在編碼、轉換及結構上的預期行為；不代表已使用 `cn.pac`／`jp.pac` 完成實機驗證，也不代表所有引擎變體均已確認。

## 手動實機測試

### PAC／SRP

- [ ] 分別使用實際日文版及簡體中文版 PAC 解包，確認 SRP 都能被識別，而不是大量變成 `.bin`。
- [ ] 日文版 SRP 匯出時選 `CP932`；簡體中文版選 `GBK／CP936`。比較實際字串，不要只憑少量共同漢字判斷。
- [ ] `Transform` 預設為 `NONE`。只有確認該 SRP 的文字 Data 還需要高低 4 位交換時才選 `NIBBLE_SWAP`。不要因選擇 GBK 而啟用交換。
- [ ] TXT v3 標頭保留 `Encoding`、`Transform`、`HeaderCount`、`ParsedCount`；編輯文字後回封，確認 Type／Flags／非文字 Data／指令順序及 footer。
- [ ] 不修改指令筆數時，確認原始 count 欄位保留；新增或刪除指令後，確認 count 更新為新筆數。
- [ ] 回封 PAC 後放入遊戲副本測試；原始遊戲檔案先備份，不要直接覆蓋唯一副本。

### VAR

- [ ] 日文版 VAR 選 CP932，簡體中文版 VAR 選 GBK／CP936，UTF-8 資料選 UTF-8。
- [ ] 檢查 `[END]` 結尾及空字串記錄，修改後回封並在遊戲副本測試。

### 安全及相容性

- [ ] 無法確認文字編碼時選 RAW，確認所有資料以 `\xHH` 保留；不要把 RAW 當成一般文字翻譯。
- [ ] 舊版 TXT v1／v2 及無標頭 croCKer TXT 使用副本測試相容性。
- [ ] 檢查輸出路徑已存在時程式會拒絕覆蓋。
- [ ] 使用損壞或截斷的 SRP／PAC 測試時，程式應回報錯誤或保留為 `.bin`，不可直接當成有效腳本處理。

## 尚未完成的驗證範圍

- [ ] 直接以目前 Repository 的 `test/cn.pac`、`test/jp.pac` 測試實際索引、SRP 內容及完整回封。這兩個 PAC 是二進位樣本，需在可讀取原始檔案的本機或 GitHub Actions 環境執行。
- [ ] 真正的遊戲載入測試。
- [ ] 不同遊戲版本、私用造字字型及未公開 SRP Type 的語意驗證。
