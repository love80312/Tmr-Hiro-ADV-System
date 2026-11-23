# SRP 參考資料檢視補充（1.0.4）

本文件補充 `REFERENCE_REVIEW.md` 的早期 SRP 整合決策。早期檢視主要依 croCKer 的 Type 註解與少量樣本，後續直接比對 VNTextPatch、SExtractor 與 GARbro 後，確認舊分類過度簡化。

## 已修正的舊假設

| 早期假設 | 1.0.4 結論 |
|---|---|
| 只依 Type 0／1 判斷文字 | 必須依完整 opcode `(Flags << 16) | Type` 判斷 |
| Type 80／Flags 21 是未知二進位 | opcode `0x00150050` 是訊息指令 |
| Type 16／Flags 20 是未知二進位 | opcode `0x00140010` 是選項指令，Data 前 3 bytes 是控制前綴 |
| Type 16／Flags 3／空 Data 是所有 SRP 的必要 footer | 不可當成跨變體的共同必要條件；以每筆 Length 與 EOF 驗證 |
| SRP TXT 可再獨立套用 NIBBLE_SWAP | PAC 解包已完成儲存層 Data nibble swap；新 TXT 不再提供第二層交換 |
| `cn.pac`／`jp.pac` 檔名可作為編碼主要判據 | 每一批 SRP 文字依實際 bytes 與使用者確認的編碼解讀，檔名不能證明 Data 編碼 |

## 參考來源

- [GARbro ArcPAC.cs](https://github.com/morkt/GARbro/blob/b09ee4570ccb1daf6ac56710ee8934dc0b8baeb0/ArcFormats/Tmr-Hiro/ArcPAC.cs)：PAC entry 的 SRP 標記與 Data nibble swap。
- [VNTextPatch TmrHiroAdvSystemCodeScript.cs](https://github.com/rafael-vasconcellos/VNTextPatch-net8/blob/152fe9984a320c02a9a5f98f6c1206361ac5ef70/VNTextPatch.Shared/Scripts/TmrHiroAdvSystem/TmrHiroAdvSystemCodeScript.cs)：訊息與選項指令處理。
- [SExtractor extract_TmrHiro.py](https://github.com/satan53x/SExtractor/blob/main/src/extract_TmrHiro.py)：讀取完整 4-byte opcode，辨識訊息 opcode、低 16 位為 0 的文字與選項 opcode；選項保留 3-byte 前綴。

## 仍需實際樣本驗證

這些參考來源確認了上表列出的 opcode 及基本資料邊界，但不能據此宣稱所有 Tmr-Hiro 遊戲版本的每個 opcode 都已完整逆向。未辨識指令仍以原始 Data bytes 保留。完整 `cn.pac`／`jp.pac` 逐筆掃描及遊戲載入測試仍是後續驗收項目。
