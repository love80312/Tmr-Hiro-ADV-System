# TmrHiroStudio 1.0.3：文字編碼與 SRP 往返修正

## 變更原則

文字編碼由使用者選擇，不需要讀取 CN.exe／JP.exe，也不以 EXE 作為 SRP 解碼的前置條件。PAC 仍然需要按其自身二進位結構解析；遊戲版本可作為使用者選擇 CP932 或 GBK 的參考，但不會被程式暗中當成編碼開關。

## 文字編碼選項

SRP／VAR 匯出提供：

- `CP932`／Shift-JIS：日文版常用編碼。
- `GBK`／`CP936`：簡體中文版常用編碼，使用 Windows-936 編碼頁。
- `UTF8`：UTF-8。
- `RAW`：不解讀文字，只使用 `\xHH` 表示原始 bytes。
- `AUTO`：保留為輔助偵測；CP932 與 GBK 共享許多漢字碼位，不能保證只靠文字特徵就可靠區分。

所有文字編碼都使用嚴格 Encoder／Decoder fallback。某些 bytes 無法在選定編碼下解碼或重新編碼成完全相同的 bytes 時，匯出改以 `\xHH` 保存，不以 `?` 或替代字元掩蓋損失。GBK／CP936 若解出 Unicode 私用區字元（PUA），也會以原始 `\xHH` 保存；沒有對應遊戲字型或碼表時，不會將私用造字假裝成通用文字。

## SRP Transform 與文字編碼分離

SRP 匯出另有 `Transform` 選項：

- `NONE`：不做 TXT 文字層 nibble swap。
- `NIBBLE_SWAP`：每個 Data byte 的高低 4 位互換。
- `AUTO`：按文字特徵選擇，僅作輔助。

預設 `CP932 + NONE`。編碼與 Transform 分開選擇，選 GBK 不會觸發 nibble swap。PAC 解包／回封本身既有的儲存層轉換仍獨立保留，不應把 PAC 層與 TXT 文字層當成同一個開關。

## TXT v3 格式

SRP TXT 範例：

```text
TMRHIRO-SRP-TEXT/3
Encoding: GBK
Transform: NONE
HeaderCount: 323
ParsedCount: 323
```

`HeaderCount` 是來源 SRP 開頭 UInt32 欄位原值；`ParsedCount` 是依完整指令邊界解析出的實際指令數。若指令筆數未變，回封保留來源 `HeaderCount`；若指令有增減，才更新成新的實際指令數。此設計避免對原始 count 欄位有特殊含義或資料不一致的樣本擅自改值。

VAR 使用 `TMRHIRO-VAR-TEXT/3`，記錄 `Encoding`。TXT 檔案本身仍以 UTF-8（無 BOM）寫入，讓繁體中文 Windows 編輯器可以穩定閱讀；標頭記錄的編碼指的是 SRP／VAR Data，不是 TXT 檔案自身的編碼。

v1／v2 TXT 格式仍保留既有解析路徑。

## PAC 中 SRP 識別

舊版只依 PAC 資源開頭的短簽章樣本判斷 SRP，容易讓只有標記但後續已截斷／結構錯誤的資源被分類成 `.srp`。新版在簽章預檢之後，會載入完整候選並檢查每筆指令長度、邊界及 `Type 16／Flags 3` footer，通過後才分類為 SRP。

如果不能通過完整驗證，資源會保留為 `.bin`，避免後續錯誤地修改它。這是保守分類策略；若其他遊戲變體的 SRP 不使用同一 footer／首筆標記，需另行加入已驗證的格式分支。

## 自動測試

新增 `tests/TmrHiroStudio.Tests`，採純合成資料，不依賴 repo 不存在的 `test/00_01.srp`。測試涵蓋 GBK／CP932／UTF-8／RAW、SRP／VAR 往返、Nibble Swap 獨立選擇、無效 bytes 跳脫、count 欄位保留／更新、舊 TXT v2 相容、PAC SRP 完整結構判斷與 PAC 層交換可逆性。

測試通過只代表合成資料行為符合預期。當前尚未宣稱 `test/cn.pac` 和 `test/jp.pac` 已完成整包分析或遊戲內載入驗證。

## 重要使用提醒

1. 日文內容先選 CP932；簡體中文內容先選 GBK／CP936。
2. SRP 的 Transform 預設 NONE；未確認前不要隨意切換。
3. 回封時不要刪除 TXT v3 標頭或手動改寫 `Encoding`／`Transform`。
4. RAW 代表無法或不打算解讀文字；`\xHH` 是 bytes，不是翻譯字串。
5. 請先對遊戲資源副本操作及測試，不要直接覆蓋原始檔。
