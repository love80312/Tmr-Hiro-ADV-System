# 自動與手動回歸測試（SRP opcode 修正版 1.0.4）

## 自動測試

GitHub Actions 會建置 WPF 主程式及 `tests/TmrHiroStudio.Tests`，再執行測試程式。任一階段失敗時，不會上傳免安裝 Artifact。測試資料由程式即時建立，不依賴本機外部 SRP 檔案。

自動測試共 18 項：

1. 使用者提供的三組 GBK 原始 Data bytes 可完整解碼並逐 byte 回封。
2. `0x00150050`（Type 80／Flags 21）訊息指令可辨識及往返。
3. `0x00140010`（Type 16／Flags 20）選項前綴與後續文字往返。
4. 低 16 位為 0 的旁白／一般文字可辨識。
5. 未知 opcode Data 以原始 bytes 保留。
6. 不含 Type 16／Flags 3 footer 的有效 SRP 可解析。
7. 原始 HeaderCount 與實際指令筆數不一致時，完整資料仍可解析且原值可保留。
8. 新增／刪除指令後更新 HeaderCount。
9. PAC SRP 標記（首筆 length=6、opcode 0x00140050）及 PAC 層 nibble swap 可逆。
10. 截斷的 SRP 指令拒絕解析。
11. 無效 GBK bytes 以 `\xHH` 保留。
12. GBK／CP936 VAR 往返。
13. CP932 VAR 往返。
14. UTF-8 VAR 往返。
15. RAW VAR 往返。
16. 舊版 SRP TXT v3 相容。
17. 舊版 SRP TXT v3 的 Type 2／3 仍依舊格式以二進位保留。
18. 舊版 VAR TXT v2 相容。

這些案例驗證程式的結構處理與保真行為；不等於已完成整份 `cn.pac`／`jp.pac` 逐筆掃描或遊戲載入測試。

## 手動實機測試

### PAC／SRP

- [ ] 以實際日文版及簡體中文版 PAC 分別解包，確認 SRP 資源可被辨識。
- [ ] SRP → TXT 時，日文內容選 CP932；簡體中文內容選 GBK／CP936。不要依賴檔名中的 `jp`／`cn` 判斷每一筆 Data 的編碼。
- [ ] 確認 TXT 使用 `TMRHIRO-SRP-TEXT/4`，並保留 Encoding、HeaderCount、ParsedCount 標頭。
- [ ] 確認 `Type 80／Flags 21` 訊息、Type 0 旁白及 `Type 16／Flags 20` 選項均可見。
- [ ] 確認選項前三個 Data bytes 以 `\xHH` 保留，標籤／跳轉目標沒有被重寫。
- [ ] 確認圖片、音效、未知 opcode 的 Data 以 `\xHH` 保留。
- [ ] TXT → SRP 後重新讀取，比對 Type、Flags、Data 及指令次序。
- [ ] 未修改指令筆數時保留來源 HeaderCount；指令筆數改變時更新 HeaderCount。
- [ ] 回封 PAC 後，以遊戲副本測試；正式資料先備份。

### VAR

- [ ] 日文 VAR 使用 CP932；簡體中文 VAR 使用 GBK／CP936；UTF-8 資料使用 UTF-8。
- [ ] 確認 `[END]` 結尾、空字串與長度欄位可正確往返。

### 安全及相容性

- [ ] 無法確認編碼時選 RAW，確認資料以 `\xHH` 保留。
- [ ] 舊版 SRP TXT v1／v2／v3 與無標頭 croCKer TXT 使用副本測試相容性。
- [ ] 已存在的輸出檔案不會被靜默覆蓋。
- [ ] 損壞或截斷的 SRP／PAC 應拒絕處理或保留為未知資源，不可輸出部分解碼結果。

## 已知尚未完成的驗證

- [ ] 對目前 Repository 中的完整 `test/cn.pac`、`test/jp.pac` 執行逐筆 opcode／編碼統計與整包 round-trip。
- [ ] 在授權遊戲副本中驗證回封 PAC 可正常載入。
- [ ] 驗證不同遊戲版本中尚未確認的 opcode、特殊文字控制碼及私用造字。

本次產出環境沒有 .NET Framework 4.8／MSBuild，因此本地未執行 Windows WPF 編譯及測試。請以 GitHub Actions 的實際結果作最終建置確認。
