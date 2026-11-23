# 自動與手動回歸測試（TmrHiroStudio 1.0.6）

## CI 自動測試

GitHub Actions 會依序還原主程式與測試專案、建置 Release x64、執行回歸測試，最後才建立並上傳免安裝 Artifact。任一建置或測試階段失敗，都不會執行成功的 Artifact 上傳步驟。

目前已驗證的 Actions 執行：

- Workflow：`Build TmrHiroStudio`
- Run：[#26](https://github.com/love80312/Tmr-Hiro-ADV-System/actions/runs/45211092068)
- Commit：`952f28cb2796bf1d55b35e8bf24ac65bc45813d8`
- 結果：Release x64 主程式建置成功、測試專案建置成功、20 項回歸測試全部通過、免安裝 Artifact 上傳成功。
- C# 建置輸出：0 Warning、0 Error。Workflow 使用的舊版 GitHub Actions 另有 Node.js 20 棄用提醒；該提醒不是 C# 編譯警告，後續已安排更新 Workflow Action 主版本。

### 自動測試項目（20 項）

1. SRP TXT v4 以 UTF-8 讀取後，依標頭 GBK 編碼正確回封。
2. VAR TXT v3 以 UTF-8 讀取。
3. 使用者提供的三組 GBK SRP Data 可正確解碼。
4. opcode `0x00150050` 訊息指令可辨識並往返。
5. opcode `0x00140010` 選項前綴與正文可往返。
6. Type 0 旁白依完整 opcode 規則辨識。
7. 未知 opcode 的原始 Data bytes 可保留。
8. 不含固定 footer 的有效 SRP 可解析。
9. HeaderCount 與實際筆數不一致時保留來源值與完整資料。
10. 新增或刪除指令後會更新 HeaderCount。
11. PAC 層 nibble swap 只處理 Data，且轉換可逆。
12. 截斷的 SRP 資料會拒絕解析。
13. GBK 無法解碼的 bytes 會以 `\xHH` 保留。
14. GBK／CP936 VAR 可往返。
15. CP932 VAR 可往返。
16. UTF-8 VAR 可往返。
17. RAW VAR 可往返。
18. 舊版 SRP TXT v3 可匯入。
19. 舊版 SRP TXT v3 的 Type 2／3 仍依舊格式保留二進位規則。
20. 舊版 VAR TXT v2 可匯入。

### 自動測試範圍限制

這 20 項主要驗證 SRP／VAR 文字容器、opcode 分類、位元組保留、編碼及 PAC 層 nibble swap。**它們不代表已自動完成整份 `test/cn.pac`／`test/jp.pac` 的逐筆掃描，也不代表 GRD PackType 1／`0xA1`／`0xA2` 的所有變體都已由 CI 覆蓋。** PAC／GRD 完整封包往返與遊戲載入仍需依下列手動清單驗證。

## 手動回歸測試

### GUI（1.0.6）

- [ ] 在 Windows 10 x64 啟動程式，確認視窗可正常顯示且沒有 XAML 載入錯誤。
- [ ] 將視窗縮小至可用最小尺寸附近，確認 PAC、GRD、SRP／VAR 頁籤內容可垂直捲動。
- [ ] 在 PAC、GRD、SRP／VAR 三個頁籤檢查輸入欄、按鈕及提示文字沒有被裁切。
- [ ] 拖曳執行記錄區上方分隔線，確認記錄區可調整高度，且不會遮住頁籤或狀態列。
- [ ] 確認工作執行時狀態、進度指示與執行記錄正常；工作結束後可繼續操作。

### PAC／SRP

- [ ] 使用日文版及簡體中文版 PAC 副本分別解包，確認資源數、檔名及 manifest 正常。
- [ ] 將原始資源未修改地回封，與來源封包比對並以遊戲副本測試。
- [ ] 日文 SRP 使用 CP932；簡體中文 SRP 使用 GBK／CP936。不要只依 `jp`／`cn` 檔名推定每筆 Data 的編碼。
- [ ] 確認訊息 opcode `0x00150050`、旁白及選項 opcode `0x00140010` 可正確顯示。
- [ ] 確認選項前三個控制 bytes、跳轉標籤與未知 opcode Data 沒有遺失或被改寫。
- [ ] TXT → SRP 後重新匯出，比對 Type、Flags、Data、指令次序及 HeaderCount。
- [ ] 將重建 SRP／VAR 放回原解包資料夾，保留 `.tmrhiro.pac.json`，再回封至新的測試 PAC。
- [ ] 使用遊戲副本確認可啟動、讀取劇情、選項、場景切換及存檔／讀檔。

### VAR

- [ ] 依來源資料選擇 CP932、GBK／CP936、UTF-8 或 RAW。
- [ ] 確認 `[END]` 結尾、空字串及各筆長度欄位可正確往返。

### GRD

- [ ] 分別取樣 PackType 1、`0xA1`、`0xA2` 來源檔案進行 GRD → PNG，檢查影像尺寸、位置與 Alpha。
- [ ] 修改 PNG 像素時保留同名 `.grd.json`，再執行 PNG → GRD。
- [ ] 只將重建後的 `.grd` 複製回原 PAC 解包資料夾；不要將 PNG 或 `.grd.json` 留在 PAC 回封資料夾第一層。
- [ ] 確認回封後遊戲中的圖像顯示正常。原始 `0xA1`／`0xA2` 會以 PackType 1 RLE 重新壓縮，需特別實測目標遊戲是否接受。

## 已知尚待完成的驗證

- [ ] 對完整 `test/cn.pac`、`test/jp.pac` 執行逐筆資源及 opcode／編碼統計。
- [ ] 以完整 PAC 副本執行未修改回封與遊戲載入測試。
- [ ] 以實際遊戲樣本確認不同版本的 GRD 壓縮類型相容性。
- [ ] 確認尚未辨識的 SRP opcode、特殊文字控制碼及私用造字資料。

自動測試通過只表示目前列出的程式回歸案例成功；未列入測試的遊戲變體及資源仍應保留原始備份並以副本驗證。
