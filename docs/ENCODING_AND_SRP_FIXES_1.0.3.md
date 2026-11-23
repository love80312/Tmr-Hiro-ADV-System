# TmrHiroStudio 1.0.3 編碼修正紀錄（已由 1.0.4 SRP opcode 修正版補充）

本文件保留 1.0.3 時期的歷史背景。若內容與目前程式行為不一致，請以 `FORMAT_NOTES.md` 及 `SRP_OPCODE_FIX_1.0.4.md` 為準。

## 1.0.3 當時的設計

1.0.3 加入 CP932、GBK／CP936、UTF-8、RAW 編碼選項，並把 SRP Data Transform 設計成文字編碼以外的獨立設定。這項設計試圖避免編碼選項暗中改變 bytes，但仍有兩個後續確認的問題：

- 文字指令只依 Type 0／1 分類，沒有依完整 32-bit opcode 辨識訊息與選項。
- 將 PAC 儲存層已處理的 nibble swap，再做成另一個手動 TXT Transform 選項，容易造成使用者重複交換。

## 1.0.4 修正後原則

- `opcode = (Flags << 16) | Type`。
- `0x00150050` 是訊息指令。
- `(opcode & 0x0000FFFF) == 0` 是旁白／一般文字指令。
- `0x00140010` 是選項指令；前三個 Data bytes 為控制欄位，必須保留。
- PAC 解包時對各筆 SRP Data 執行儲存層 nibble swap。已解包的 SRP 不再提供第二層 Transform。
- SRP 依每筆 Length 及檔案邊界解析至 EOF，不強制要求單一 footer 型式。
- 新 TXT 使用 `TMRHIRO-SRP-TEXT/4`；舊 v1／v2／v3 仍保留匯入相容。

## 編碼選擇

- 日文常用 CP932／Shift-JIS。
- 簡體中文資料可選 GBK／CP936。
- UTF-8 與 RAW 保留給相應樣本。
- AUTO 僅供參考；CP932 與 GBK 共用部分漢字碼位，必要時應明確選擇。

## 注意

TXT 檔案本身以 UTF-8 儲存，標頭中的 `Encoding` 才是 SRP／VAR Data 的編碼。不要將不同遊戲變體的指令結構互相套用，也不要把 Fortune Cookie Select 的逐行加密方式套用至本 SRP 指令流。
