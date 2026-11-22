# 格式實作筆記（1.0.2）

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
- 檔名以固定長度 CP932 位元組儲存，欄位長度依編碼後 byte count 計算。
- 絕對位置為 `data_offset + relative_offset`；所有資源範圍必須在檔案內。
- manifest 保存原始名稱 bytes、資源順序及 PAC 版本；刪除解包資源即不再回封該資源。

### SRP 在 PAC 中的轉換層

PAC 儲存層轉換只作用在 SRP 指令 Data，不作用在 4-byte count、指令 length、type、flags 或 footer。解包與回封由 `ScriptCodec.TransformPacScriptLayer()` 成對處理。這一層與 SRP 文字顯示時可能需要處理的 nibble 狀態分開。

## GRD

GRD 固定標頭 0x20 bytes：

| Offset | 欄位 |
|---:|---|
| 0x00 | UInt16 format；低位元組版本 1／2，高位元組壓縮類型 |
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

通道依序 Alpha（若有）、Red、Green、Blue。已支援壓縮類型：

- `0x01`：RLE
- `0xA1`：Huffman 後 RLE
- `0xA2`：Huffman 後 LZ77

PNG → GRD 新建資料使用 RLE；不承諾保留原壓縮位元流。驗證依解碼後 BGRA 像素及 metadata，而非比較壓縮資料 hash。

## SRP

- 前 4 bytes 是指令數欄位。
- 每筆指令：UInt16 length、Int16 type、Int16 flags、`length - 4` bytes Data。
- 已觀察到 Type 16／Flags 3／空 Data 為終止指令；解析以 footer 與檔案邊界驗證。
- 參考 `croCKer/Srp.cs` 對 Type 0／1 的描述是遊戲文字／選項；Type 2／3 是圖像／音效。由於遊戲分支可能不同，1.0.2 只把 Type 0／1 當文字候選，Type 2／3／80／未知型別一律保留 bytes。

### SRP TXT v2

```text
TMRHIRO-SRP-TEXT/2
Encoding: CP932
Transform: NONE

Type: 0
Flags: 0
Data: 日本語の台詞
```

- `Encoding`：`CP932`、`UTF8` 或 `RAW`。
- `Transform`：`NONE` 或 `NIBBLE_SWAP`。
- 匯出時只用 Type 0／1 資料比較候選；回封依標頭反向轉換。
- `RAW` 時文字 Data 必須以連續 `\xHH` 表示，避免臆測。
- Type 2／3／80／未知型別的 Data 使用 `\xHH` 保留位元組。
- `\g{F040|提示字形}` 是 EUDC 明確 token，只回寫指定兩個位元組。

沒有工具標頭的 croCKer 舊 TXT 使用 CP932 及 nibble swap 相容模式。舊版本專案 `TMRHIRO-SRP-TEXT/1` 使用 CP932、Transform NONE。新 TXT 請勿刪除 v2 標頭。

## VAR

- 記錄格式：UInt16 byte length + byte payload。
- 最後一筆必須為 ASCII `[END]`。
- 新建每筆長度不得超過 32767 bytes。

### VAR TXT v2

```text
TMRHIRO-VAR-TEXT/2
Encoding: CP932
日本語の変数名
[END]
```

- `Encoding`：`CP932`、`UTF8` 或 `RAW`。
- 不在 metadata 與資料間插入空白分隔行，第一筆空字串因此可保留。
- 無法可靠判定編碼時，以 `RAW` + `\xHH` 形式保留 bytes。

## 不做的假設

- 不將 `AGE_System` 宣稱為已確認的正式引擎名稱。
- 不將 PAC 未辨識資源強行當成已知格式。
- 不把 Type 80 或 Flags 20／21 臆測成特定子欄位。
- 不在沒有字型／字碼表證據時猜測 EUDC 私用造字字形。
