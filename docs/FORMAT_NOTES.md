# 格式實作筆記

本文記錄本專案依公開格式研究整理的結構。未知欄位不擅自賦予未經證實的意義。

## PAC

PAC 標頭共 7 bytes：

| 欄位 | 型別 | 長度 |
|---|---:|---:|
| 資源數量 | Int16 little-endian | 2 |
| 固定檔名欄位長度 | Byte | 1 |
| Data offset | UInt32 little-endian | 4 |

版本判斷：

- V1：`data_offset = 7 + count × (name_length + 8)`；每筆 index 為檔名 + UInt32 相對位移 + UInt32 資源長度。
- V2：`data_offset = 7 + count × (name_length + 12)`；每筆 index 為檔名 + Int64 相對位移 + UInt32 資源長度。
- 檔名以固定長度 Shift-JIS（CP932）位元組儲存，不可用 Unicode 字元數取代編碼後位元組數。
- 資源絕對位置為 `data_offset + relative_offset`。

PAC SRP 資料層：封包索引所指向的 SRP 資源會在每筆指令 Data 範圍內進行 nibble swap。指令標頭包含 UInt16 length、Int16 type、Int16 flags；每筆 Data 長度為 `length - 4`，Data 起點在 6-byte 指令標頭之後。轉換只套用 Data，不套用 SRP 標頭、length、type、flags。

## GRD

GRD 固定標頭 0x20 bytes：

| Offset | 欄位 |
|---:|---|
| 0x00 | UInt16 format；低位元組為格式版本 1／2，高位元組為壓縮類型 |
| 0x02 | UInt16 screen width |
| 0x04 | UInt16 screen height |
| 0x06 | UInt16 BPP（24／32） |
| 0x08 | UInt16 left |
| 0x0A | UInt16 right |
| 0x0C | UInt16 top |
| 0x0E | UInt16 bottom |
| 0x10 | UInt32 alpha channel size |
| 0x14 | UInt32 red channel size |
| 0x18 | UInt32 green channel size |
| 0x1C | UInt32 blue channel size |

通道資料依序為 Alpha（若有）、Red、Green、Blue。影像寬高由座標差取絕對值。公開解碼器可辨識的壓縮類型為：

- `0x01`：RLE。
- `0xA1`：Huffman 解碼後再 RLE。
- `0xA2`：Huffman 解碼後再 LZ77。

新建 GRD 採用 `0x01` RLE。這避免在缺乏對應編碼器的情況下臆測 Huffman/LZ77 編碼規則；原始低位元組版本仍由 sidecar 保存。

## SRP

- 前 4 bytes 是指令數欄位。
- 每筆指令包含 UInt16 length、Int16 type、Int16 flags 及 `length - 4` bytes Data。
- 已觀察到 Type 16／Flags 3 的終止指令；本工具解析時以終止指令及檔案邊界為依據，避免只依賴可能不一致的 count。
- 腳本 Data 的 nibble swap 是 SRP 腳本格式層；PAC 封包本身另有一層 SRP Data 轉換，兩者分開處理。

## VAR

- 重複的記錄格式為 UInt16 byte length + Shift-JIS bytes。
- 檔案最後一筆為 `[END]`。
- 新建時使用小端序長度，單筆長度限制在原格式可表示範圍。

## 不做的假設

- 不將 `AGE_System` 宣稱為已確認的正式引擎名稱。
- 不將 PAC 未辨識資源強行當成已知格式；以 `.bin` 保留原始 bytes。
- 不宣稱 PNG → GRD 能保留原始壓縮位元流；只重新產生可逆的 RLE 通道資料及保留 metadata。
