# TmrHiroStudio

**Tmr-Hiro ADV System 資源工具**，以繁體中文 WPF 介面處理 PAC 封包、GRD 圖像、SRP 腳本及 VAR 變數資料。

- 目標系統：Windows 10 x64
- 執行環境：.NET Framework 4.8
- 介面：WPF
- 發布：GitHub Actions Artifact，免安裝 ZIP
- 建置：GitHub Actions；可直接透過 GitHub 網頁操作
- 版本：1.0.3

> 引擎名稱依公開格式研究採用 Tmr-Hiro ADV System；`AGE_System` 是否為正式內部名稱仍未獲充分證實。

## 功能

### PAC 封包

- 支援 PAC Version 1／2 解包及回封。
- 解包保留原始索引順序、檔名位元組及版本資訊於 `.tmrhiro.pac.json`。
- 解包辨識 GRD、SRP、VAR、OGG、TWAV；無法確認的資料以 `.bin` 保留。
- SRP 的 PAC 儲存層 nibble swap 由 PAC 解包與回封成對處理。
- 回封可自動沿用 manifest 版本，或明確選擇 V1／V2。
- PAC 不儲存子資料夾結構；回封只處理解包目錄第一層資源。

### GRD 圖像

- 支援 RLE、Huffman + RLE、Huffman + LZ77 解碼。
- PNG → GRD 使用可驗證的 RLE 編碼；不宣稱保留原壓縮位元流。
- 支援 24／32 bpp 與 Alpha 通道。
- PNG sidecar `.grd.json` 保存原始畫布大小、圖像座標、版本及通道 metadata。
- GRD 回封會重新解碼並逐像素比較 BGRA；驗證通過才提交輸出。
- 批次作業先處理至暫存目錄，所有檔案成功後才提交；既有輸出不會被覆蓋。

### SRP／VAR 腳本

- SRP／VAR TXT 一律以 UTF-8（無 BOM）儲存；TXT 內的 `Encoding` 決定資源 Data 的原始文字編碼。
- 手動支援 CP932／Shift-JIS、GBK／CP936、UTF-8、RAW；預設 CP932。簡體中文版請在匯出前選 GBK／CP936。
- SRP 的 `Transform` 與文字編碼獨立，可選 NONE、NIBBLE_SWAP 或自動偵測；預設 NONE，不會因選擇 GBK 而自動交換 nibble。
- 新版 SRP／VAR TXT 使用 v3 標頭，回封完全依標頭使用的編碼與 Transform；舊版 v1／v2 TXT 仍可回編。
- Type 0／1 才按文字資料處理；Type 2／3、Type 80 及未知 Type 以 `\xHH` 保留位元組，避免把圖片／音效／未知二進位誤當文字。
- CP932 EUDC 私用造字可由 `cp932-pua-map.tsv` 提供顯示提示；TXT 使用明確 `\g{F040|字形}` token，避免一般同字漢字被反向轉成 EUDC。GBK／CP936 的未知私用造字區沒有通用字形表，會保留為 `\xHH` 原始 bytes。
- 選擇 RAW 時只輸出 `\xHH` 位元組序列；適合保留未知／非文字 Data，不應把 RAW 內容當一般文字編輯。
- 支援先前版本 TXT 標頭及 croCKer 舊格式的回編相容路徑；舊 croCKer 無標頭 TXT 固定按 CP932 讀取，新格式標頭／UTF-8 BOM 按 UTF-8 讀取，避免靠「UTF-8 是否剛好能解碼」誤判日文 CP932。

## SRP 編碼及轉換說明

1.0.3 將文字編碼與 SRP Data Transform 完全分離。匯出面板預設為 `CP932 + NONE`；簡體中文版請改選 `GBK／CP936`。直接取得的 SRP 若已知 Data 經過 nibble swap，再獨立選擇 `NIBBLE_SWAP`。自動偵測只作輔助，不能取代已知遊戲版本的手動設定。

新 SRP TXT 標頭範例：

```text
TMRHIRO-SRP-TEXT/3
Encoding: GBK
Transform: NONE
HeaderCount: 323
ParsedCount: 323
```

`HeaderCount` 保留原始 SRP count 欄位；只要指令筆數未變，回封保留該欄位原值。若新增／刪除指令，回封才將 count 更新為實際指令數。`ParsedCount` 是來源實際解析到的指令數。Type／Flags、非文字 Data、指令順序及 footer 會依原內容重建。

VAR TXT 使用 `TMRHIRO-VAR-TEXT/3`，並在標頭記錄 `Encoding`。CP932 EUDC 對應仍只在 CP932 模式使用；GBK 無法解碼或無法精確往返的位元組會輸出為 `\xHH`，避免以替代字元破壞資料。

Nibble swap 是每個 byte 的高低 4 位交換，不是字元編碼。PAC 層的 SRP 轉換與 TXT 標頭的 SRP Data Transform 是兩個不同步驟，不可因為選 GBK 就推定要交換。

## 私用造字對照表

`src/TmrHiroStudio/cp932-pua-map.tsv` 是 UTF-8 TSV，欄位為：

```text
CP932 位元組<TAB>PUA 碼位<TAB>字形提示
```

例如格式示意（不是實際遊戲字元對照）：

```text
F040<TAB>E000<TAB>漢
```

若有可信映射，TXT 中會輸出 `\g{F040|漢}`。回封只看明確 token 的 CP932 位元組，不會把一般文字「漢」轉成 `F040`。目前附帶表只有註解範本，沒有猜測性的實際映射；要顯示遊戲專用漢字，仍需字型、EUDC 檔或已驗證的碼表。

## 輸出安全

- SRP／VAR 批次匯出及回封會先處理所有輸入，再以暫存資料夾提交。
- PAC／GRD 批次解包、解碼及回封也先完成工作目錄，再提交。
- 預設拒絕覆蓋既有 TXT、SRP、VAR、PNG、GRD 及解包目錄，避免覆蓋翻譯稿或原始資料。
- 若批次提交期間發生錯誤，程式會嘗試回滾；若回滾也失敗，會保留暫存目錄並在錯誤訊息中顯示路徑。
- PAC 單檔回封仍使用 Save File Dialog 的覆蓋提示，並以同目錄暫存檔完成替換。
- 所有格式解析皆檢查長度、檔案邊界及結尾標記；不要將未驗證的輸出直接覆蓋遊戲原檔。

## 使用方式

### PAC

1. 在「PAC 封包」分頁選擇 PAC 檔案或資料夾。
2. 指定輸出資料夾並執行解包。
3. 修改各 PAC 子資料夾中的資源；保留 `.tmrhiro.pac.json`。
4. 選擇子資料夾進行回封。自動模式會優先沿用 manifest 記錄的版本。

### GRD

1. 執行 GRD → PNG；每張 PNG 會附帶 `.grd.json`。
2. 使用支援 Alpha 的編輯器修改 PNG，保留 sidecar。
3. 執行 PNG → GRD，輸出新 GRD。
4. 程式會拒絕覆蓋既有輸出，請使用空白輸出資料夾或另選檔名。

### SRP／VAR

1. 選擇 `.srp`／`.var` 或資料夾，使用個別匯出功能；也可選擇「SRP + VAR → TXT」。
2. 使用 UTF-8 編輯器修改 TXT。
3. 新版 SRP／VAR TXT 請保留 v3 標頭與所有欄位；舊版 v1／v2 標頭仍可回編。
4. 使用 TXT → SRP／VAR 回封，再將資源放回 PAC 解包資料夾。

若 TXT 標頭顯示 `Encoding: RAW`，代表程式無法可靠判斷文字編碼；內容以 `\xHH` 保留，請勿直接把十六進位序列改成猜測的漢字。

## GitHub Actions 建置

修正設計請見 `docs/ENCODING_AND_SRP_FIXES_1.0.3.md`；檔案覆蓋／新增及網頁上傳方式請見 `docs/UPLOAD_INSTRUCTIONS_1.0.3.md`。

1. 將本專案檔案上傳至 Repository 根目錄，保留 `.github/workflows/build.yml` 路徑。
2. 開啟 Actions，執行 **Build TmrHiroStudio**。
3. Workflow 會 restore、build Release x64、執行自動回歸測試，全部成功後才準備 Artifact。
4. 下載 `TmrHiroStudio-Win10-x64`，解壓縮一次即可看到 `TmrHiroStudio.exe`、設定檔（若有）、`cp932-pua-map.tsv` 及文件。

## 專案結構

```text
Tmr-Hiro-ADV-System/
├─ .github/workflows/build.yml
├─ docs/
│  ├─ ENCODING_AND_SRP_FIXES_1.0.3.md
│  ├─ FORMAT_NOTES.md
│  ├─ TEST_PLAN.md
│  └─ UPLOAD_INSTRUCTIONS_1.0.3.md
├─ src/TmrHiroStudio/
│  ├─ Core/
│  │  ├─ BinaryUtil.cs
│  │  ├─ GrdCodec.cs
│  │  ├─ JsonUtil.cs
│  │  ├─ PacCodec.cs
│  │  ├─ PuaCharacterMap.cs
│  │  ├─ ScriptCodec.cs
│  │  └─ ScriptTextCodec.cs
│  ├─ MainWindow.xaml
│  ├─ MainWindow.xaml.cs
│  ├─ MainWindow.BatchEnhancements.cs
│  ├─ MainWindow.ScriptEnhancements.cs
│  ├─ MainWindow.ScriptEncodingSettings.cs
│  ├─ InternalsVisibleTo.cs
│  ├─ cp932-pua-map.tsv
│  └─ TmrHiroStudio.csproj
├─ tests/TmrHiroStudio.Tests/
│  ├─ Program.cs
│  └─ TmrHiroStudio.Tests.csproj
├─ TmrHiroStudio.sln
├─ LICENSE
├─ THIRD_PARTY_NOTICES.md
└─ README.md
```

## 已知限制

- 手動模式依使用者選擇的編碼與 Transform 執行；只有選擇 AUTO 時才使用特徵評分。CP932 與 GBK 共享許多漢字碼位，AUTO 不保證能分辨簡體中文與日文，建議按遊戲語系手動指定。
- Type 2／3 按參考分類保守視為二進位；Type 80 及未知 Type 不猜測語意。
- EUDC 字形提示表刻意不填猜測資料。沒有實際字型／碼表時，CP932 自訂字可能以 `\xHH` 或明確 `\g{}` token 表示；GBK／CP936 私用造字則以 `\xHH` 保留。
- GRD 新輸出會重新編碼成 RLE，不保留 Huffman／LZ77 原始壓縮位元流；驗收以像素與 metadata 為準。
- 自動測試使用合成資料，涵蓋 CP932／GBK／UTF-8／RAW、SRP／VAR 往返、獨立 nibble swap、SRP count 欄位保留／更新、舊版 TXT v2 相容，以及 PAC SRP 完整結構檢查；測試項目及實機測試清單見 `docs/TEST_PLAN.md`，仍不能取代真實 PAC／遊戲實機測試。
- 請先用合法持有的遊戲資源副本測試，確認遊戲能讀取重建檔案後再替換正式資源。

## 授權與參考

本專案程式碼以 MIT License 發布。外部參考專案保留其各自授權；詳見 `THIRD_PARTY_NOTICES.md` 及 `docs/REFERENCE_REVIEW.md`。
