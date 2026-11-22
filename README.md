# TmrHiroStudio

**Tmr-Hiro ADV System 資源工具**，以繁體中文 WPF 介面處理 PAC 封包、GRD 圖像、SRP 腳本及 VAR 變數資料。

- 目標系統：Windows 10 x64
- 執行環境：.NET Framework 4.8
- 介面：WPF
- 發布：GitHub Actions Artifact，免安裝 ZIP
- 建置：GitHub Actions；可直接透過 GitHub 網頁操作
- 版本：1.0.2

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

- SRP ↔ UTF-8 TXT；VAR ↔ UTF-8 TXT。
- SRP 匯出會比較 CP932／UTF-8 及 Data nibble swap／不交換候選，避免將所有 SRP 一律當作相同轉換狀態。
- SRP v2 TXT 標頭記錄實際採用的 `Encoding` 與 `Transform`。回封依標頭反向處理，不能刪除或改寫這些欄位。
- VAR v2 TXT 標頭記錄 `Encoding`，支援 CP932、UTF-8、RAW。
- Type 0／1 才按文字資料處理；Type 2／3、Type 80 及未知 Type 以 `\xHH` 保留位元組，避免把圖片／音效／未知二進位誤當文字。
- CP932 EUDC 私用造字可由 `cp932-pua-map.tsv` 提供顯示提示；TXT 使用明確 `\g{F040|字形}` token，避免一般同字漢字被反向轉成 EUDC。
- CP932／UTF-8 無法可靠判斷時使用 RAW 十六進位表示，仍可原樣回封，但不應把 RAW Data 當一般文字編輯。
- 支援先前版本 TXT 標頭及 croCKer 舊格式的回編相容路徑；舊 croCKer 無標頭 TXT 固定按 CP932 讀取，新格式標頭／UTF-8 BOM 按 UTF-8 讀取，避免靠「UTF-8 是否剛好能解碼」誤判日文 CP932。

## SRP 編碼及轉換說明

SRP 可能經過不同來源流程：直接取得的 SRP 與從 PAC 解包後的 SRP，其 Data 可能處於不同 nibble swap 階段。舊版程式固定交換或固定不交換，會使其中一種來源解碼錯誤。

1.0.2 匯出時只用 Type 0／1 的資料作候選評估，嘗試 CP932／UTF-8，並比較 nibble swap／不交換。結果寫入 TXT，例如：

```text
TMRHIRO-SRP-TEXT/2
Encoding: CP932
Transform: NONE

Type: 0
Flags: 0
Data: 日本語の台詞
```

另一種來源可能記錄 `Transform: NIBBLE_SWAP`。請保留 TXT 標頭，回封時程式會使用該欄位還原位元組。此自動判斷是依文字特徵選出最合理候選，不是對所有遊戲變體的數學保證；RAW 是無法可靠判讀時的資料保護結果。

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
3. SRP v2 請保留 `TMRHIRO-SRP-TEXT/2`、`Encoding:` 及 `Transform:` 標頭；VAR v2 請保留 `TMRHIRO-VAR-TEXT/2` 及 `Encoding:`。
4. 使用 TXT → SRP／VAR 回封，再將資源放回 PAC 解包資料夾。

若 TXT 標頭顯示 `Encoding: RAW`，代表程式無法可靠判斷文字編碼；內容以 `\xHH` 保留，請勿直接把十六進位序列改成猜測的漢字。

## GitHub Actions 建置

本次完整覆蓋／新增檔案清單及網頁上傳方式請見 `docs/UPLOAD_INSTRUCTIONS_1.0.2.md`。

1. 將本專案檔案上傳至 Repository 根目錄，保留 `.github/workflows/build.yml` 路徑。
2. 開啟 Actions，執行 **Build TmrHiroStudio**。
3. Workflow 會 restore、build Release x64、執行自動回歸測試，全部成功後才準備 Artifact。
4. 下載 `TmrHiroStudio-Win10-x64`，解壓縮一次即可看到 `TmrHiroStudio.exe`、設定檔（若有）、`cp932-pua-map.tsv` 及文件。

Artifact 不會再包含一個手動產生的內層 ZIP。

## 專案結構

```text
Tmr-Hiro-ADV-System/
├─ .github/workflows/build.yml
├─ docs/
│  ├─ FIXES_1.0.2.md
│  ├─ FORMAT_NOTES.md
│  ├─ TEST_PLAN.md
│  └─ UPLOAD_INSTRUCTIONS_1.0.2.md
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
│  ├─ InternalsVisibleTo.cs
│  ├─ cp932-pua-map.tsv
│  └─ TmrHiroStudio.csproj
├─ tests/TmrHiroStudio.Tests/
│  ├─ Program.cs
│  └─ TmrHiroStudio.Tests.csproj
├─ test/00_01.srp
├─ LICENSE
├─ THIRD_PARTY_NOTICES.md
└─ README.md
```

## 已知限制

- SRP 編碼／轉換判斷採特徵評分；不同遊戲分支或非典型腳本可能需要新增已知樣本及格式規則。
- Type 2／3 按參考分類保守視為二進位；Type 80 及未知 Type 不猜測語意。
- EUDC 字形提示表刻意不填猜測資料。沒有實際字型／碼表時，部分簡體或日文遊戲自訂字仍會以 `\xHH` 或 `\g{}` 表示。
- GRD 新輸出會重新編碼成 RLE，不保留 Huffman／LZ77 原始壓縮位元流；驗收以像素與 metadata 為準。
- 自動測試涵蓋固定 SRP 樣本、合成日文 CP932／UTF-8、兩種 nibble 狀態、VAR、PUA 安全表示、原子寫入與 GRD 像素往返；尚不能取代所有商業遊戲實機測試。
- 請先用合法持有的遊戲資源副本測試，確認遊戲能讀取重建檔案後再替換正式資源。

## 授權與參考

本專案程式碼以 MIT License 發布。外部參考專案保留其各自授權；詳見 `THIRD_PARTY_NOTICES.md` 及 `docs/REFERENCE_REVIEW.md`。
