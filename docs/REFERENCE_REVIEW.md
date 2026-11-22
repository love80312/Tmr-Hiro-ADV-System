# 六個參考專案的程式碼檢視與整合決策

本專案沒有把六個倉庫的 GUI／程式碼直接拼接，而是依其公開格式資訊與可觀察行為，重寫共同的 PAC、GRD、SRP、VAR 核心。原因包括目標平台不同、同一功能有重複實作、舊程式的資料邊界檢查不足，以及 GPL／MIT／未明確授權的授權條件並不一致。

## 逐一檢視

### 1. Chenx221/Tmr-Hiro_PAC_Repack

**用途：** PAC V1/V2 回封參考；有 PAC 結構筆記、SRP nibble transform 與 Shift-JIS 工具函式。

**需要更新的部分：**

- 補齊索引範圍、資源範圍、檔案數及整數溢位檢查。
- 在腳本區塊處理前驗證 record 長度與終止位置。
- 避免無條件以整個資源大小配置記憶體。
- 固定檔名欄位長度必須按 Shift-JIS 編碼後的 byte count 計算。
- 回封索引順序、原始檔名位元組與原始 PAC 版本應可保留。

**整合決策：** 依公開 PAC 結構獨立實作 V1/V2 parser/packer；使用 manifest 記錄原始名稱 bytes、版本及索引順序。

### 2. HOKORISAMA/Tmr-hiro-grd-tools

**用途：** PAC 解包及 GRD → PNG 解碼；包含 RLE、Huffman、LZ77 通道處理。

**需要更新的部分：**

- Python `ExPac.py` 使用 UTF-8 解讀 PAC 索引名稱，與 Shift-JIS 固定欄位格式不一致。
- 多個解壓迴圈缺少完整的來源／目的長度檢查，損壞資料可能造成索引越界或不合理配置。
- GRD 解碼未提供 PNG → GRD 的完整回封器。
- C# 專案引用了與目前實際解碼用途不完全相符的額外影像套件。

**整合決策：** 以 CP932 讀取索引；對 RLE/Huffman/LZ77 增加輸出長度及位移邊界檢查；PNG 影像使用 WPF/WIC 內建 PNG codec，避免額外 ImageSharp／System.Drawing 執行依賴。

### 3. crskycode/TmrHiro_Tool

**用途：** GRD metadata 及 PNG → GRD RLE 編碼參考。

**需要更新的部分：**

- 原編碼器固定寫入 `0x0101`，沒有保留原始 GRD 標頭低位元組的格式版本。
- 像素先轉成 `Argb32`，24-bit 分支卻以三 byte step 取樣四 byte 像素資料，通道索引不正確。
- metadata 匯出未包含原始 format 標記，不能據此恢復原始格式版本。
- 對 PNG 尺寸、metadata 座標及壓縮輸出缺少完整驗證。

**整合決策：** 以明確 BGRA byte channel 建立 RLE；sidecar 保存完整必要 metadata；24／32 bpp 分別輸出；保留原始版本 1／2，但新壓縮資料使用可驗證的 RLE 類型。

### 4. TheVNConnoisseur/croCKer

**用途：** SRP／VAR 解碼、文字匯出及回編參考。

**需要更新的部分：**

- SRP 文字格式以固定行數分隔欄位，遇到 Data 內含換行會失去結構。
- SRP 匯出使用 `TrimStart` 移除開頭 Tab／CR／LF，可能改變原始指令內容。
- SRP 回編仰賴行數推算指令數，文字中出現額外換行或資料格式不完整時容易錯位。
- 部分邊界檢查仰賴 BinaryReader／陣列索引例外，而不是先驗證長度。
- VAR 長度使用 signed 16-bit，文字輸出追加寫入可能把舊結果留在檔案後方。

**整合決策：** 新文字格式使用專用標頭與明確跳脫，並將無法解碼的原始 byte 以 `\xHH` 表示；SRP 解析以實際結尾指令及檔案長度為準；VAR 回編驗證每筆長度與 `[END]`。

### 5. nanami5270/TmrHiro_Tool

**用途：** 同一套 TmrHiro_Tool／GrdTool 的另一個公開來源。

**檢視結果：** 在檢查的版本中，其 README 與 GrdTool 檔案內容和 `crskycode/TmrHiro_Tool` 相同，並非另一個獨立的編碼器實作。

**整合決策：** 不重複合併相同程式碼；其 GRD 風險與修正方式合併在上一項處理。

### 6. TheVNConnoisseur/PACkager

**用途：** WPF PAC V1/V2 解包／回封與 PAC 層 SRP nibble transform。

**需要更新的部分：**

- 回封器以 `.Length`（UTF-16 字元數）計算名稱欄位，而不是 Shift-JIS byte count；日文名稱可能截斷或破壞索引。
- 解包時副檔名辨識部分依賴封包檔名含有 `grd`，容易使 GRD 被辨識成無副檔名資料。
- 腳本轉換依賴 header count；格式筆記指出部分遊戲 count 與實際結構可能不一致。
- 原始回封需要人工選 V1/V2，缺乏自動保留原始索引 metadata 的機制。

**整合決策：** GRD 依實際 header 和 channel sizes 辨識；manifest 保留 PAC 版本、原始名稱 bytes 和順序；SRP 以有效指令邊界及 Type 16／Flags 3 footer 驗證。

## 新技術／架構決策

| 項目 | 決策 | 原因 |
|---|---|---|
| GUI | WPF | Windows 原生桌面 GUI，無需額外 GUI runtime |
| Target Framework | .NET Framework 4.8 | 對 Windows 10 22H2 Pro 相容性及免安裝需求較穩妥 |
| Build SDK | .NET 10 SDK | 僅在 GitHub Actions 編譯時使用；不代表執行檔需要 .NET 10 Runtime |
| CPU target | x64 | 與目標 Windows 10 x64 一致 |
| 圖像處理 | WPF/WIC `BitmapDecoder`／`PngBitmapEncoder` | 不需額外 ImageSharp 或 System.Drawing.Common 執行依賴 |
| 序列化 | `DataContractJsonSerializer` | 使用 .NET Framework 內建元件保存 manifest／metadata |
| 發布 | GitHub Actions Artifact ZIP | 不建立 MSI、不要求安裝流程，更新可直接下載新 artifact |

## 授權決策

參考專案中同時存在 MIT、GPL-3.0 及檢視時未找到授權檔的倉庫。為避免把不同授權的程式碼不加區分地併入，本專案只將它們作為格式研究及行為參考，重新撰寫核心程式碼，並獨立採用 MIT License。未來若要直接複製或改寫任一參考倉庫的程式碼，必須重新檢視其授權並更新本專案授權安排。


## 1.0.1 GUI 修正

- `MainWindow.xaml.cs` 的工作啟動介面改為 `Func<Action>`。路徑欄位及 PAC 版本等 WPF 控制項只在 UI 執行緒擷取；`Task.Run` 僅執行已擷取的資料與核心方法。
- `PathHistory.cs` 將上次檔案／資料夾位置保存至 LocalApplicationData，避免對免安裝程式所在目錄寫入設定。
- `NativeFolderPicker.cs` 使用 Windows Shell `IFileDialog` 的 `FOS_PICKFOLDERS`，取代傳統 WinForms 資料夾瀏覽視窗。
- 工作期間改為停用功能分頁，而非停用整個 Window；記錄區保持可用。
- 移除 `UseWindowsForms`，不再因資料夾選擇器而引入 WinForms 相依。
