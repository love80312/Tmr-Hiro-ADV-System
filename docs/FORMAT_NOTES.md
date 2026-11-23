# 格式實作筆記（SRP opcode 修正版）

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
- 檔名以固定長度 CP932 位元組儲存。
- 絕對位置為 `data_offset + relative_offset`；所有資源範圍必須在檔案內。
- manifest 保存原始名稱 bytes、資源順序及 PAC 版本。

### PAC 內 SRP 的儲存層

SRP 的 PAC 儲存層 nibble swap 只作用在每筆指令 Data，不作用在 UInt32 count、UInt16 length、Type 或 Flags。解包與回封各執行一次相同的可逆 nibble swap。PAC 解包後的 `.srp` 已是完成這一層還原的資料，SRP → TXT 不應再執行第二次交換。

## SRP

SRP 由 UInt32 count 欄位開始，後續每筆指令格式如下：

| 欄位 | 型別 | 長度 |
|---|---|---:|
| Length | UInt16 little-endian | 2 |
| Type | UInt16 little-endian | 2 |
| Flags | UInt16 little-endian | 2 |
| Data | bytes | `Length - 4` |

Length 計算 Type、Flags 與 Data 的總長度，不包含前方 Length 本身。

### Opcode

Type 與 Flags 必須合併成一個 32-bit opcode：

```text
opcode = (Flags << 16) | Type
```

已由上游 SExtractor／VNTextPatch 實作確認的文字分類：

| Opcode 條件 | 用途 | Data 處理 |
|---|---|---|
| `opcode == 0x00150050` | 訊息 | 依指定編碼解碼完整欄位字串 |
| `(opcode & 0x0000FFFF) == 0` | 旁白／一般文字 | 依指定編碼解碼 |
| `opcode == 0x00140010` | 選項 | 前 3 bytes 保留為控制欄位，其餘解碼為選項文字 |
| 其他 opcode | 控制、圖片、音效及其他指令 | 原始 bytes 以 `\xHH` 保留 |

訊息可能包含多個逗號分隔欄位（例如 voice／name／message）。本工具不擅自拆欄或重排欄位，整段原樣顯示，以避免不同遊戲版本欄位順序不一致造成資料遺失。

選項 opcode `0x00140010` 的前三個 Data bytes 是非顯示欄位。TXT 以三組 `\xHH` 顯示並原樣寫回；後續文字中的選項標籤／跳轉目標亦保持原值。翻譯時只修改選項文字，不要修改逗號分隔符及標籤。

### 記錄數與結尾

- UInt32 count 會保存在 TXT v4 的 `HeaderCount` 欄位。
- 實際解析以每筆 Length 與檔案邊界為準，持續解析至 EOF。
- 不強制要求 `Type 16／Flags 3／空 Data` 作為所有版本共用的必要 footer；遇到這種記錄時仍會當作一般未知指令保留。
- 未修改指令筆數時保留原始 HeaderCount；新增／刪除指令後才將 HeaderCount 更新為實際筆數。
- 任何指令標頭截斷、Length 小於 4、Data 越界或指令數超過安全上限都會拒絕解析。

### SRP TXT v4

```text
TMRHIRO-SRP-TEXT/4
Encoding: GBK
HeaderCount: 3
ParsedCount: 3

Type: 0
Flags: 0
Data: 在迷迷糊糊中……
```

- `Encoding`：`CP932`、`GBK`／`CP936`、`UTF8` 或 `RAW`。
- `HeaderCount`：來源檔案開頭 UInt32 原值。
- `ParsedCount`：實際依指令長度解析出的筆數。
- TXT 檔案本身以 UTF-8 輸出；`Encoding` 指 SRP Data 編碼。
- 不再有新格式 `Transform` 欄位。PAC 解包已處理儲存層 nibble swap。
- 不能可靠解碼或不能同 bytes 往返的字元以 `\xHH` 保留。
- 選項前三個控制 bytes 以 `\xHH` 保留。
- 未知指令 Data 僅接受連續 `\xHH` 表示，避免誤將二進位內容當成可編輯文字。

### 舊 SRP TXT 相容

- `TMRHIRO-SRP-TEXT/1`、`/2`、`/3` 仍可匯入。
- 舊 v2／v3 的 `Transform: NONE` 或 `NIBBLE_SWAP` 會依舊格式反向處理；其舊文字分類維持 Type 0／1，Type 2／3 仍要求 `\xHH` 二進位表示。
- 舊 v1 與沒有工具標頭的 croCKer TXT 保留各自舊版 Type 0／1／2／3 相容路徑；無標頭 croCKer TXT 仍採 CP932／nibble-swap 相容處理。
- 新匯出一律使用 v4，不再輸出第二層 Transform 選項。

## VAR

- 記錄格式：UInt16 byte length + byte payload。
- 最後一筆必須為 ASCII `[END]`。
- 新建每筆長度不得超過 32767 bytes。
- VAR TXT v3 格式與既有讀寫方式保留，不受本次 SRP opcode 修正影響。

## 不做的假設

- 不以 `cn.pac`／`jp.pac` 檔名決定每一筆 SRP Data 的文字編碼。
- 不將所有 Type 80、Type 16 或其他單一 Type 一概視為同一種內容。
- 不把所有 SRP 變體都強制套用同一種 footer。
- 不把 Fortune Cookie Select 的逐行 `0x0A` 加密格式套用至此 SRP 指令流。
- 不將未知 opcode 的二進位參數猜測成文字。
