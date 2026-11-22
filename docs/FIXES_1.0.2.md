# TmrHiroStudio 1.0.2 修正說明

## SRP／VAR 文字解碼及日文亂碼

### 1. 不再假設所有 SRP Data 都處於同一 nibble 狀態

舊版 SRP 轉換曾固定交換或固定不交換 Data；但是 PAC 解包層與 SRP 腳本 Data 層是不同階段，直接取得的 SRP 和 PAC 解包後的 SRP 不能盲目套用同一假設。

新版匯出會使用 Type 0／1 Data 評估候選：

- CP932、不交換
- UTF-8、不交換
- CP932、nibble swap
- UTF-8、nibble swap

採用分數最高且達到可信門檻的候選。Type 0／1 內少量遊戲控制碼不會單獨讓整份日文資料降級成 RAW，而會以 `\uXXXX` 保留。TXT v2 會保存 `Encoding:` 及 `Transform:`，回封時套用相同的反向轉換。候選不夠可信時輸出 `Encoding: RAW`，以 `\xHH` 保留原始位元組，不假裝已解碼成明文。

舊 croCKer 無格式標頭 TXT 固定按 CP932 讀取；本工具 v1／v2 格式標頭或 UTF-8 BOM 按 UTF-8 讀取。這避免只因某段 CP932 位元組碰巧符合 UTF-8，就把舊日文稿誤解成亂碼。

這是保護策略，不是對所有自訂引擎變體的絕對保證；若出現錯誤候選，需要新增對應遊戲樣本及明確規則。

### 2. VAR 同時辨識 CP932／UTF-8

VAR v2 TXT 記錄 `Encoding:`。以嚴格解碼避免替代字元造成不可逆損失。格式標頭後不插入分隔空行，避免合法的第一筆空字串被略過。

### 3. Type 2／3 不當成文字

新 SRP v2 只將 Type 0／1 當作文字候選。Type 2／3（參考資料將其描述為圖像／音效）及 Type 80／未知型別均以原始 bytes 表示，避免把二進位碰巧解碼成假日文或控制字元。

舊版 v1 及無標頭 croCKer TXT 保留相容讀取邏輯；舊資料可能有不同文字處理規則，請先以副本測試。

### 4. PUA／EUDC 不再和一般漢字互相誤轉

舊版映射表將某個一般替代字直接反向映射到 EUDC，會使同一個普通漢字在整份 TXT 中被錯誤改寫。新版改用明確 token：

```text
\g{F040|漢}
```

只有明確 token 會回寫 F0 40；一般字元「漢」仍依 TXT 標頭編碼。尚未映射的 EUDC 以 `\xHH` 保留。

### 5. SRP 舊、新轉換入口統一

`ScriptCodec` 保留舊方法作相容入口，但轉換工作統一委派至 `ScriptTextCodec`。WPF XAML 直接綁定增強事件，不再於 `OnContentRendered` 搜尋按鈕、替換事件或動態插入合併按鈕。

### 6. 批次輸出保護

- SRP／VAR、PAC、GRD 批次輸出先寫到暫存目錄。
- 先檢查所有目的路徑，既有檔案或資料夾不覆蓋。
- 批次提交中途失敗會嘗試回滾；回滾失敗時保留工作目錄，錯誤訊息會提供位置。
- GRD PNG 與 sidecar 成對產生；PNG → GRD 不會改寫來源 PNG 的 sidecar。
- PAC 回封使用隨機暫存檔並確認來源檔大小沒有在建索引後變更。

### 7. 測試與建置

新增 `tests/TmrHiroStudio.Tests` 控制台回歸測試。Workflow 在打包前執行測試，涵蓋：

- 固定 `test/00_01.srp` 的結構及 TXT 往返
- CP932 日文 SRP，交換／不交換兩種來源
- 混有遊戲控制碼的 CP932 日文 SRP
- UTF-8 日文 SRP
- Type 2／3 二進位保護及無法辨識資料的 RAW 降級
- CP932／UTF-8 日文 VAR
- VAR 空字串記錄
- 一般漢字不會被 PUA 映射誤轉，EUDC 明確 token 可回封
- 原子寫入拒絕覆蓋
- GRD PNG 像素／Alpha 往返

Actions 成功代表自動測試通過及應用程式可編譯；仍不代表所有遊戲的實機載入測試均通過。
