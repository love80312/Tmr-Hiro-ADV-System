# TmrHiroStudio 1.0.3 Hotfix 01

## 對應失敗紀錄

GitHub Actions：`45015376776`

應用程式與測試專案的 Release x64 編譯都成功。失敗發生在 `Run format regression tests` 的第二項：

`GBK／CP936 SRP 與 NIBBLE_SWAP 獨立往返`

錯誤：`offset 0xA：預期 0x1A，實際 0xA1`。

## 根本原因

`DecompileSrp()` 會將二進位 Data 套用一次選定的 Transform，產生 TXT 中可編輯的顯示 bytes。回封時，`CompileSrp()` 本來就會將 TXT bytes 套用一次相同 Transform，還原儲存層 bytes。

但舊版 `ParseSrpText()` 在解析 TXT 時也先執行了一次 `ApplyTransform(payload, transform)`。因此回封路徑實際套用兩次 NIBBLE_SWAP。NIBBLE_SWAP 是自身反向，兩次交換會抵銷，最後輸出未交換的 bytes。

## 修正

- 移除 `ParseSrpText()` 內多餘的 `ApplyTransform()`。
- `ParseSrpText()` 僅解析 TXT 顯示／編輯後的 bytes。
- `CompileSrp()` 成為唯一將 TXT bytes 轉回 SRP 儲存層的地方，且只套用一次 Transform。
- 保留 `NONE`、`NIBBLE_SWAP`、CP932、GBK／CP936、UTF-8、RAW 與 TXT v1／v2／v3 格式行為。
- 回歸測試同時放入 Type 0 文字和 Type 2 二進位 Data，確認兩者都經過正確且唯一的一次反向轉換。

## 驗證狀態

已依照失敗日誌確認根因，並完成修正與逐 byte 轉換邏輯檢查。此工作環境沒有 .NET Framework 4.8／WPF 編譯工具，故本包不宣稱已在本機執行 C# 測試。上傳後應重新執行 GitHub Actions；只有測試全數通過，才代表此 Hotfix 已由 CI 驗證。
