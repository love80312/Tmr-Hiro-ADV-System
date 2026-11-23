# TmrHiroStudio 1.0.6

TmrHiroStudio 是供 **Tmr-Hiro ADV System** 遊戲資源使用的 Windows x64 圖形工具，處理 PAC 封包、GRD 圖像，以及 SRP／VAR 腳本。

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
- 可解碼 PackType 1、`0xA1`、`0xA2`，輸出 PNG 及對應的 `.grd.json` 中繼資料。
- PNG → GRD 會依 PNG 像素重新建立通道資料，並在寫出前執行解碼往返驗證。
- **目前 PNG → GRD 統一使用 RLE（PackType 1）重新壓縮**。原始 GRD 若是 `0xA1`／`0xA2`，回封後的壓縮格式會改為 PackType 1；Version 低位元組則沿用中繼資料。
- 24 bpp GRD 不含 Alpha 通道。若 PNG 有透明像素，而 sidecar 指定 24 bpp，工具會拒絕輸出，避免無聲丟失透明資訊。
- 沒有 `.grd.json` 時會套用新建圖像的預設 metadata（32 bpp、左上角起始，並建立 Alpha 平面）。修改既有遊戲圖像時，請保留原始 sidecar。
- 對原始 `0xA1`／`0xA2` GRD 的重新壓縮結果，仍應以目標遊戲實際讀取測試確認；不要假設所有遊戲版本都接受相同的輸出格式。

### SRP／VAR 腳本

- SRP 使用完整 32-bit opcode 判斷訊息、旁白、選項及其他指令。
- 選項前綴、跳轉標籤與未知二進位 Data 會保留；未知 Data 以 `\xHH` 表示。
- 新版 SRP TXT 使用 `TMRHIRO-SRP-TEXT/4`，VAR TXT 使用 `TMRHIRO-VAR-TEXT/3`；TXT 檔案本身以 UTF-8 讀取。
- SRP／VAR Data 可選 CP932（Shift-JIS）、GBK／CP936、UTF-8 或 RAW。編碼資訊寫在 TXT 標頭，回封時依標頭處理。
- AUTO 僅供參考。CP932 與 GBK 有共用字元碼位，不能保證自動判斷正確；重要檔案請明確選擇編碼。
- 支援批次匯出 SRP + VAR，以及先完整解析再提交的 TXT → SRP + VAR 批次回封。

## 修圖流程：PAC → GRD → PNG → GRD → PAC

1. 在「PAC 封包」頁解包原始 PAC。保留輸出資料夾內的 `.tmrhiro.pac.json`。
2. 在「GRD 圖像」頁，以 PAC 解包資料夾作為 GRD 輸入，輸出 PNG。每張 PNG 會有對應的 `.grd.json` 中繼資料。
3. 使用圖像編輯器修改 PNG。**保持 PNG 檔名不變，並保留對應的 `.grd.json`**。只修改像素時，通常不需要編輯 JSON。
4. 執行 PNG → GRD，輸出到另一個暫存資料夾。
5. 將產生的 GRD 檔複製回 PAC 解包資料夾，覆蓋同名的原始 GRD。不要把 PNG 或 `.grd.json` 放進 PAC 資源資料夾。
6. 回到「PAC 封包」頁，選擇原本的 PAC 解包資料夾回封。保留 manifest，PAC 版本選「自動（優先使用 manifest）」。
7. 將新 PAC 放在遊戲資源的備份／測試位置，確認遊戲顯示正常後再替換正式檔案。

**重要：** 不要直接把「PNG 輸出資料夾」或「GRD 輸出資料夾」當成 PAC 回封資料夾。PAC 回封應使用原本含有 `.tmrhiro.pac.json` 的解包資料夾，並把修好的同名 GRD 放回其中。PAC 打包器不會自動排除 `.png`，若 PNG 留在回封資料夾第一層，可能會被當成額外資源加入封包。

## GRD JSON 欄位說明

以下為中繼資料格式示意，實際數值依來源 GRD 而不同：

```json
{
  "Format": 257,
  "ScreenWidth": 1280,
  "ScreenHeight": 720,
  "Bpp": 24,
  "Left": 0,
  "Right": 1280,
  "Top": 0,
  "Bottom": 720,
  "AlphaSize": 0,
  "RSize": 818984,
  "GSize": 823546,
  "BSize": 825038
}
```

| 欄位 | 用途 |
|---|---|
| `Format` | 高位元組為 PackType，低位元組為 Version；例如十進位 `257` 即十六進位 `0x0101`，代表 PackType 1、Version 1。 |
| `ScreenWidth`、`ScreenHeight` | 原始畫布尺寸。 |
| `Bpp` | 色彩深度，目前支援 24 或 32。 |
| `Left`、`Right`、`Top`、`Bottom` | 圖像在原始畫布中的範圍／位置。 |
| `AlphaSize` | Alpha 通道壓縮資料大小；為 0 時表示沒有 Alpha 資料平面。 |
| `RSize`、`GSize`、`BSize` | RGB 各通道壓縮資料大小。 |

回封時通道大小會依實際輸出資料重新計算，**不要手動修改 `AlphaSize`、`RSize`、`GSize` 或 `BSize`**。僅修改圖像像素時，請保留原始中繼資料，讓工具沿用畫布及位置資訊。

## SRP／VAR TXT 編碼

- TXT 檔案容器固定以 UTF-8 讀取，`Encoding:` 標頭指定的是 SRP／VAR 內部 Data 的文字編碼，兩者不可混為一談。
- `CP932` 用於日文遊戲文字，`GBK`／`CP936` 用於簡體中文資料；請依原始資源或實際測試選擇。
- `RAW` 用於不轉換成一般文字的位元組資料；未知或無法解碼的資料會以跳脫位元組形式表示。
- `AUTO` 無法可靠區分所有 CP932 與 GBK 字元，尤其是兩種編碼共用的位元組序列。自動判斷結果不可視為保證正確。
- 編輯 TXT 時請保留檔案標頭、區塊順序、控制資料及跳脫位元組；不要刪除工具用來識別指令的結構內容。

## 建置環境

- Windows x64
- .NET Framework 4.8 目標環境
- 可建置 SDK-style WPF 專案的 .NET SDK
- 專案使用 `Microsoft.NETFramework.ReferenceAssemblies.net48` 參考組件套件，以利在 CI 環境建置

本工具以 WPF 建立圖形介面。使用者應先保留原始 PAC／GRD／SRP／VAR 備份，並在替換遊戲資源前以目標遊戲進行實際測試。

## 版本 1.0.6

- 調整主視窗最小尺寸，讓較小解析度的螢幕也能操作。
- PAC、GRD 與 SRP／VAR 頁面加入捲動區，避免視窗高度或寬度不足時控制項無法操作。
- 執行記錄區調整為較精簡的預設高度，並加入可拖曳分隔線以調整記錄區大小。
- 更新版本顯示與專案版本資訊。

## 相容性與風險提示

本工具會處理二進位封包及遊戲腳本。即使檔案可成功解包或回封，也不代表所有內容都能被每個遊戲版本正確讀取。建議以複製檔案進行測試，確認遊戲可正常啟動、切換場景、顯示文字與圖像後，再替換正式資源。任何格式不明或未經實測的資源，都應保留原始備份。

## 參考資料與專案文件

- [第三方參考專案、研究範圍與授權狀態](THIRD_PARTY_NOTICES.md)
- [參考專案檢視與整合決策](docs/REFERENCE_REVIEW.md)
- [SRP opcode 參考資料補充](docs/REFERENCE_REVIEW_SRP_UPDATE_1.0.4.md)
- [PAC／GRD／SRP／VAR 格式實作筆記](docs/FORMAT_NOTES.md)
- [自動與手動回歸測試計畫](docs/TEST_PLAN.md)
- [文件索引與歷史紀錄](docs/README.md)
