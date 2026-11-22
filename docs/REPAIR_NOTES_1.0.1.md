# SRP／VAR／GRD 修正說明

本修正包以 `main` 分支 `de5be03cf7f37a029343e08b3f7caaf9c3dd6e6c` 為基準。

## 1. GRD 像素往返驗證

`GrdCodec.cs` 在產生 GRD 後，會先在記憶體中重新解析並解碼，將結果與 WPF 正規化的 BGRA32 PNG 像素緩衝區逐 byte 比較。接著寫入暫存檔，再從暫存檔讀取並執行第二次驗證；只有兩次驗證成功才替換正式輸出檔。

- 原始 GRD 的壓縮類型可能為 RLE、Huffman + RLE 或 Huffman + LZ77；重建統一採可逆 RLE，因此不以 MD5 或壓縮位元組相同作為驗收條件。
- 24bpp 沒有 Alpha 通道。若 PNG 含非完全不透明像素，回封會中止，不會靜默刪除透明度。
- 32bpp 即使原始 GRD 沒有 Alpha 通道，只要 PNG 新增透明像素，就建立 Alpha 通道。
- 已有 `.grd.json` 會更新成實際輸出格式及通道大小；未帶 sidecar 的 PNG 維持原有新圖像處理方式。

## 2. SRP + VAR 批次匯出

執行程式後，在「SRP／VAR 腳本」分頁的匯出按鈕列會增加「SRP + VAR → TXT」。

- 輸入可為單一 `.srp`／`.var` 檔案，或資料夾。
- 資料夾模式遞迴搜尋兩種副檔名，並保留相對目錄結構。
- SRP 輸出為 `.srp.txt`，VAR 輸出為 `.var.txt`，不會混成同名 `.txt`。
- 先解析所有輸入並檢查目的路徑，再開始寫出；遇到無效資源或目標名稱衝突時，不會先寫出部分批次結果。
- 單獨 SRP／VAR 匯出與回封按鈕也會改用同一套轉換器。

## 3. CP932 EUDC／PUA 雙向對照

`cp932-pua-map.tsv` 是放在 EXE 同一目錄的 UTF-8 對照表，欄位為：

```text
CP932 位元組<TAB>PUA 碼位<TAB>替代字元
```

例如格式（純示意，不是實際遊戲字碼對照）：

```text
F040<TAB>E000<TAB>漢
```

每筆資料會在啟動轉換時驗證：位元組位於 CP932 EUDC 範圍、碼位位於 U+E000–U+E757，且目前 CP932 解碼器確實將該位元組解成指定 PUA 碼位。替代字元須為單一 BMP 字元，且不能重複。

匯出時，已對照的 EUDC 位元組轉成替代字元；回封時，替代字元轉回相同的兩個 CP932 位元組。未登錄的 EUDC 字元會輸出成 `\xHH\xHH`，不猜字義；若使用者直接輸入未登錄 PUA 字元，回封會中止並提示建立對照。

**目前對照表刻意留空。** 現有對話只有若干私用字元樣本，沒有遊戲字型、EUDC 字碼表或可驗證的 byte-to-glyph 對照；把猜測字義寫入預設表會造成錯字或錯誤回封。取得原始字型／造字資料後，才能補入實際映射。

請使用 UTF-8 編輯 `cp932-pua-map.tsv`，填入三欄並以 Tab 分隔；不要刪除表頭註解。修改後重新啟動程式。

## 4. SRP Type 80

- 參考 `croCKer/Srp.cs` 的格式註解，Type 0 被描述為遊戲文字、Type 1 為選項文字、Type 2 為圖像載入、Type 3 為音效；參考程式本身也註明這些判讀未必適用所有遊戲。
- 因此 Type 0／1／2／3 可以嘗試 CP932 文字及 EUDC 對照；Type 80 及其他未知型別都以 `\xHH` 保留原始資料。Type 2／3 的用途註解不代表每個遊戲的 Data 一定是文字。
- 對 Type 0–3，只有 CP932 解碼後再編碼能還原完全相同 bytes 的片段才顯示為文字；無法往返的片段保留 `\xHH`。Type 80／未知型別在回封時只接受連續 `\xHH` 序列，避免不明二進位被當作文字重新編碼。
- Type 與 Flags 數值原樣保留；本修正沒有臆測 Flags 20／21 的功能，也沒有宣稱已反向解析出 Type 80 的子欄位結構。
- 目前文字格式中的 Type／Flags 以十進位輸出；因此樣本的 Flags 20／21 是十進位 20／21（0x14／0x15），兩者只有最低位不同。這只能描述數值差異，不能據此判定該位的語意。
- 目前提供的 Type 80 樣本中，Flags 20 的 Data 解碼後為 2 bytes（`10 00`）；Flags 21 的 Data 為 14 bytes（`3F FA 59 AE 39 94 D9 DE 5F D4 E8 89 59 2F`）。這兩筆的長度與內容都不同，僅憑這兩筆不能判定 Type 80 的子欄位、壓縮方式或 Flags bit 0 的作用。

目前查到的公開參考只說明 Type 0–3 的大致用途，沒有提供 Type 80 或 Flags 20／21 的對照表。要進一步解析 Type 80，仍需要多份不同 Flags、已知內容與遊戲執行行為相互對照的 SRP 樣本。現階段的正確策略是保持位元組可逆，而不是自動解碼成看似可讀的文字。

## 5. 安裝／部署檔案

需覆蓋／新增下列檔案：

- `src/TmrHiroStudio/Core/GrdCodec.cs`
- `src/TmrHiroStudio/Core/PuaCharacterMap.cs`（新增）
- `src/TmrHiroStudio/Core/ScriptTextCodec.cs`（新增）
- `src/TmrHiroStudio/MainWindow.ScriptEnhancements.cs`（新增）
- `src/TmrHiroStudio/cp932-pua-map.tsv`（新增）
- `src/TmrHiroStudio/TmrHiroStudio.csproj`
- `.github/workflows/build.yml`

SDK-style 專案會自動包含新增的 `.cs` 檔案。GitHub Actions 會把 TSV 複製到免安裝 ZIP，讓執行檔能讀取對照表。

## 6. 驗證限制

本修正環境沒有 Windows WPF／.NET Framework 4.8 編譯工具，因此沒有宣稱 Windows 編譯成功，也未以遊戲原始 GRD／SRP／VAR 樣本進行實機驗收。請以 GitHub Actions 的 Windows build 結果確認編譯，再用資源副本執行無修改匯出／回封測試。Type 80 語意與實際 PUA 對照仍需原始遊戲資料驗證。
