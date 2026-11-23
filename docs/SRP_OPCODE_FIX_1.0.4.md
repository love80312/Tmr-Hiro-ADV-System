# TmrHiroStudio 1.0.4：SRP opcode 與資料解碼重整

## 問題來源

舊版 `ScriptTextCodec.IsTextInstructionType()` 只用 Type 0／1 判斷文字。這會漏掉真正的訊息與選項指令，因為 Tmr-Hiro SRP 的指令識別依賴 Type 與 Flags 組成的 32-bit opcode。

上游參考：

- GARbro `ArcFormats/Tmr-Hiro/ArcPAC.cs`：PAC 中 SRP 的識別與 Data nibble swap。
- VNTextPatch `TmrHiroAdvSystemCodeScript.cs`：訊息 opcode `0x00150050`、低 16 位為 0 的文字指令、選項 opcode `0x00140010`。
- SExtractor `extract_TmrHiro.py`：使用 `readInt(..., 4)` 取得完整 opcode，並將選項文字前 3 bytes 略過。

## 修正內容

1. `SrpInstruction.Opcode` 將 Flags 放在高 16 位、Type 放在低 16 位。
2. 文字 opcode 按上游規則分類，不再只看 Type。
3. 訊息完整欄位保留原順序，不猜測逗號欄位代表 name／voice 的固定順序。
4. 選項 Data 的 3-byte 前綴以 `\xHH` 保存，後續字串解碼；回封不重建跳轉標籤。
5. 解析依 UInt16 Length 與檔案邊界讀取至 EOF，不強制 Type 16／Flags 3 footer。
6. PAC nibble swap 維持在 `ScriptCodec.TransformPacScriptLayer()`，只作用在每筆 Data。SRP TXT 匯出移除第二層 Transform UI。
7. 新 TXT 使用 v4；舊 v1／v2／v3 與無標頭 croCKer TXT 仍保留讀取相容。
8. VAR 處理保留，不跟 SRP 指令 opcode 混用。

## 使用者提供的 bytes

測試加入三組原始 bytes，固定使用 GBK／CP936 解碼，驗證以下內容可以完整呈現並逐 byte 回封。這些測試是已提供樣本的回歸測試，不等於已驗證整份 cn.pac／jp.pac 所有資源。

## 回封安全性

- 修改文字只改變對應 Data bytes 與 Length。
- Type、Flags、未知指令、選項前綴與選項標籤原樣保留。
- 未修改指令數時保留來源 HeaderCount；指令數變更才更新。
- PAC 回封階段才重新套用 PAC 儲存層 nibble swap。

## 驗證限制

測試已加入合成 opcode 案例及三組提供的 GBK bytes。產出環境沒有 .NET Framework 4.8／MSBuild，不能在此直接執行 Windows WPF 編譯或測試；請以 GitHub Actions 結果驗證最終建置。
