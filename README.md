# TmrHiroStudio

**Tmr-Hiro ADV System 資源整合工具**，以繁體中文 WPF 介面整合 PAC 封包、GRD 圖像及 SRP／VAR 腳本處理。

- 目標平台：Windows 10 x64
- 執行階段：.NET Framework 4.8（Windows 10 22H2 已內建）
- 發布形式：免安裝 ZIP，不使用安裝程式
- 路徑選擇：記憶上次使用位置，優先目前欄位路徑，其次上次位置，最後程式目錄
- 資料夾選擇：Windows 原生 Explorer 樣式資料夾選擇視窗
- 編譯方式：GitHub Actions（純網頁操作即可編譯）
- 專案版本：1.0.1

> 引擎名稱採用公開格式研究中較常見的 **Tmr-Hiro ADV System**。`AGE_System` 尚未獲得足夠證據確認為正式名稱，因此不將兩者宣稱為已證實的別名。

## 功能

### PAC 封包

- 解包及回封 PAC Version 1、Version 2。
- Version 1 使用 32-bit 相對位移；Version 2 使用 64-bit 相對位移。
- 使用 Shift-JIS（CP932）解析索引檔名，回封時以**位元組數**計算固定名稱欄位，不以 .NET 字元數代替。
- 解包時依資源標頭辨識 `.grd`、`.srp`、`.ogg`、`.twav`、`.var`；無法確認的資源保留為 `.bin`。
- PAC 解包目錄會產生 `.tmrhiro.pac.json`，記錄原始 PAC 版本、索引順序及原始檔名位元組。回封時可保留原始索引順序及名稱；刪除資源檔即不會將該資源放回封包。
- PAC 的 SRP 資料層 nibble swap 會在解包及回封時成對處理。
- 回封支援明確選擇 V1／V2，或依 manifest 自動沿用原版本。
- 回封時會略過 `.grd.json`、`.srp.txt`、`.var.txt` 中繼／編輯文字檔，避免誤當成遊戲資源加入封包。

### GRD 圖像

- GRD → PNG：支援 RLE（`0x01`）、Huffman + RLE（`0xA1`）、Huffman + LZ77（`0xA2`）通道解碼。
- PNG → GRD：產生使用 RLE（`0x01`）的 GRD，並保留 metadata 中的 GRD 版本低位元組（1／2）、畫布尺寸、圖像座標及 24／32 bpp 設定。
- 支援 24-bit、32-bit（含／不含 Alpha）。
- 每張匯出的 PNG 旁會建立同名 `.grd.json` 中繼資料。回封時會自動讀取，以保留原始圖像位置及畫布參數。
- 對於沒有 sidecar 的新 PNG，使用 PNG 尺寸建立新的 32-bit GRD。

**注意：PNG → GRD 會重新編碼成 RLE，不會保留原本的 Huffman／LZ77 壓縮位元流。** 這是正常的格式重建；解碼後的圖像內容及必要 metadata 才是保留目標。

### SRP／VAR 腳本

- SRP ↔ UTF-8 TXT。
- VAR ↔ UTF-8 TXT。
- SRP 保留 Type、Flags、Data 與終止指令（Type 16／Flags 3）。解析以實際終止指令及檔案邊界為準，不完全信任可能錯誤的指令數欄位。
- SRP／VAR 文字格式使用明確的格式標頭及跳脫序列，保留反斜線、換行、Tab、NUL 等控制字元。
- Shift-JIS 無法解碼的二進位內容會輸出為 `\xHH`，避免因替代字元造成資料遺失。
- 也可讀取 `croCKer` 舊格式文字：SRP 的 `Type:`／`Flags:`／`Data:` 結構，以及以 `[END]` 結尾的 VAR TXT。

## 1.0.1 修正內容

- 修正背景工作直接讀取 WPF `TextBox`／`ComboBox` 所造成的跨執行緒例外。所有 UI 參數都在 UI 執行緒擷取，背景工作只接收一般資料。
- 工作執行時只停用功能分頁，執行記錄仍可檢視及清除；狀態列顯示工作狀態及忙碌指示。
- 檔案選擇器會優先使用目前欄位的有效路徑，其次使用相同類型上次選擇位置，最後回退到程式所在目錄。
- 新增路徑記憶，設定存放於 `%LocalAppData%\TmrHiroStudio\settings.json`，不會寫入程式目錄。
- 資料夾選擇改用 Windows Shell `IFileDialog`，不再使用傳統 WinForms `FolderBrowserDialog`。
- 移除專案對 Windows Forms 的建置依賴，保留 WPF／.NET Framework 4.8／x64。
- 輸出預設位置改為程式目錄下的 `Output`，不會在啟動時自動建立；實際執行工作時才建立。

## 使用方式

### PAC 解包／回封

1. 開啟 **PAC 封包** 分頁。
2. 選擇一個 `.pac` 檔案，或選擇包含多個 `.pac` 的資料夾。
3. 設定輸出位置並執行解包。每個 PAC 會建立自己的子資料夾。
4. 修改解包資料夾內的資源檔。不要刪除 `.tmrhiro.pac.json`，除非你打算以新封包方式建立索引。
5. 選擇該解包資料夾進行回封。選「自動」會沿用 manifest 記錄的 PAC 版本。

PAC 本身不儲存子資料夾結構，回封時只會處理解包資料夾第一層的資源檔；子資料夾會略過並在記錄中提示。

### GRD 圖像編輯

1. 在 **GRD 圖像** 分頁，選擇 GRD 檔案或資料夾，執行 GRD → PNG。
2. 使用支援 Alpha 的繪圖軟體編輯 PNG。不要刪除對應的 `.grd.json`。
3. 選擇編輯後的 PNG 檔案或資料夾，執行 PNG → GRD。
4. 將輸出的 `.grd` 放回 PAC 解包資料夾，再回封 PAC。

單獨編輯既有 GRD 時，請將 PNG 與 `.grd.json` 放在同一資料夾，確保程式讀得到 metadata。

### SRP／VAR 文字編輯

1. 選擇 `.srp`／`.var` 資源或資料夾，分別執行 SRP → TXT 或 VAR → TXT。
2. 使用 UTF-8 編輯器修改輸出的 TXT，保留檔案第一行格式標頭及 `Type:`／`Flags:`／`Data:` 結構；VAR 最後一筆必須是 `[END]`。
3. 選擇 TXT 檔案或資料夾，分別執行 TXT → SRP 或 TXT → VAR。
4. 將編譯出的二進位資源放回 PAC 解包資料夾，再回封 PAC。

新格式文字檔的範例標頭：

```text
TMRHIRO-SRP-TEXT/1
```

```text
TMRHIRO-VAR-TEXT/1
```

新格式使用 `\n`、`\r`、`\t`、`\0`、`\\`、`\xHH` 表示特殊字元或原始位元組。不要移除格式標頭，也不要將 `\xHH` 當成一般文字改寫。

## 建置（GitHub 網頁操作）

1. 在 GitHub 建立新的空白 repository，建議名稱 `TmrHiroStudio`。
2. 將本專案 ZIP 解壓縮，將**最外層資料夾內的全部檔案及資料夾**上傳到 repository 根目錄。
3. 確認 `.github/workflows/build.yml` 位於 repository 的 `.github/workflows/` 下。
4. 開啟 repository 的 **Actions** 頁面，選擇 **Build TmrHiroStudio**。
5. 第一次上傳可由 `workflow_dispatch` 手動啟動；之後更新 `main` 分支也會自動編譯。
6. 成功後進入該次 workflow run 頁面，在 **Artifacts** 下載 `TmrHiroStudio-Win10-x64`。
7. 解壓縮 ZIP，直接執行 `TmrHiroStudio.exe`。

此專案以 .NET Framework 4.8 為目標，不使用 .NET Desktop Runtime 自包含部署。Windows 10 22H2 已內建 .NET Framework 4.8，因此發佈檔不需額外安裝 .NET Runtime。若目標電腦的 .NET Framework 4.8 系統元件被移除或損壞，仍須先修復 Windows 元件。

## 專案結構

```text
TmrHiroStudio/
├─ .github/
│  └─ workflows/
│     └─ build.yml
├─ docs/
│  ├─ FIXES_1.0.1.md
│  ├─ FORMAT_NOTES.md
│  ├─ REFERENCE_REVIEW.md
│  └─ TEST_PLAN.md
├─ src/
│  └─ TmrHiroStudio/
│     ├─ App.xaml
│     ├─ App.xaml.cs
│     ├─ MainWindow.xaml
│     ├─ MainWindow.xaml.cs
│     ├─ NativeFolderPicker.cs
│     ├─ PathHistory.cs
│     ├─ app.manifest
│     ├─ App.config
│     ├─ Core/
│     │  ├─ BinaryUtil.cs
│     │  ├─ GrdCodec.cs
│     │  ├─ JsonUtil.cs
│     │  ├─ PacCodec.cs
│     │  └─ ScriptCodec.cs
│     ├─ Models/
│     │  └─ Metadata.cs
│     └─ TmrHiroStudio.csproj
├─ .gitignore
├─ LICENSE
├─ TmrHiroStudio.sln
├─ THIRD_PARTY_NOTICES.md
└─ README.md
```

## 已知限制及安全原則

- 目前只針對上述 Tmr-Hiro PAC／GRD／SRP／VAR 結構；不保證所有使用相似副檔名的遊戲封包都相容。
- `.twav` 目前僅辨識並原樣解包／回封，未實作 WAV 播放或轉檔；不臆測其未確認的音訊標頭欄位。
- PAC 解包會拒絕不合法的索引範圍、重複輸出檔名及不合理資源大小；不會直接覆寫既有解包目錄。
- GRD 解碼會檢查標頭、通道長度、影像尺寸及壓縮輸出邊界。
- SRP／VAR 回編會檢查指令／字串長度、結尾標記及資料邊界。
- 請先以遊戲資源副本測試，確認遊戲能正常讀取重建的 PAC／GRD／SRP／VAR 後，再替換正式檔案。
- 此專案不包含任何遊戲資源、遊戲執行檔或商業素材。

## 參考研究

六個倉庫的逐項程式碼檢視及技術取捨，請見 [docs/REFERENCE_REVIEW.md](docs/REFERENCE_REVIEW.md)；1.0.1 GUI 修正說明請見 [docs/FIXES_1.0.1.md](docs/FIXES_1.0.1.md)；手動回歸測試項目請見 [docs/TEST_PLAN.md](docs/TEST_PLAN.md)；格式欄位筆記請見 [docs/FORMAT_NOTES.md](docs/FORMAT_NOTES.md)。

本專案為重新整理後的獨立實作，未將下列專案的 GPL 程式碼直接合併進來。這些連結用於格式結構、相容性行為及功能範圍的研究：

- [Chenx221/Tmr-Hiro_PAC_Repack](https://github.com/Chenx221/Tmr-Hiro_PAC_Repack)
- [HOKORISAMA/Tmr-hiro-grd-tools](https://github.com/HOKORISAMA/Tmr-hiro-grd-tools)
- [crskycode/TmrHiro_Tool](https://github.com/crskycode/TmrHiro_Tool)
- [TheVNConnoisseur/croCKer](https://github.com/TheVNConnoisseur/croCKer)
- [nanami5270/TmrHiro_Tool](https://github.com/nanami5270/TmrHiro_Tool)
- [TheVNConnoisseur/PACkager](https://github.com/TheVNConnoisseur/PACkager)
- [GARbro Tmr-Hiro GRD decoder](https://github.com/morkt/GARbro/blob/master/ArcFormats/Tmr-Hiro/ImageGRD.cs)
- [GARbro Tmr-Hiro PAC reader](https://github.com/morkt/GARbro/blob/master/ArcFormats/Tmr-Hiro/ArcPAC.cs)

## 授權

本專案新撰寫的程式碼以 MIT License 發布。參考連結中的其他專案各自保留其原有授權；詳見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
