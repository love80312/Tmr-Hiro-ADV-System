using System;
using System.IO;
using System.Text;

namespace TmrHiroStudio.Core
{
    /// <summary>
    /// Reads the human-readable TXT container used by TmrHiroStudio.
    /// The code page in an SRP/VAR header describes the binary payload,
    /// not the encoding of the TXT file itself. Current exported TXT files
    /// are UTF-8; legacy unheaded croCKer files are CP932.
    /// </summary>
    internal static class ScriptTextFileCodec
    {
        public static string ReadText(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("TXT 路徑不可為空。", nameof(path));

            return Decode(File.ReadAllBytes(path));
        }

        internal static string Decode(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            // UTF-8 BOM: decode strictly and never silently replace damaged bytes.
            if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
                return BinaryUtil.Utf8Strict.GetString(bytes, 3, bytes.Length - 3);

            // UTF-16 BOMs are accepted as a convenience for manually saved TXT files.
            if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
                return new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
                return new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);

            if (HasKnownUtf8Header(bytes))
            {
                try
                {
                    return BinaryUtil.Utf8Strict.GetString(bytes);
                }
                catch (DecoderFallbackException ex)
                {
                    throw new InvalidDataException(
                        "這份 TmrHiroStudio TXT 使用新版格式標頭，但檔案內容不是有效的 UTF-8。" +
                        "請將 TXT 檔案本身儲存為 UTF-8；標頭中的 Encoding: GBK／CP932 是 SRP／VAR 內文的編碼，不是 TXT 檔案編碼。", ex);
                }
            }

            // Legacy croCKer TXT has no TmrHiroStudio header and is CP932.
            // Do not guess GBK here: guessing can silently damage Japanese text.
            try
            {
                return BinaryUtil.ShiftJisStrict.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException(
                    "無格式標頭的舊版 TXT 無法以 CP932 解碼。請確認來源文字檔編碼，" +
                    "新版 TmrHiroStudio TXT 請保留 TMRHIRO-SRP-TEXT／TMRHIRO-VAR-TEXT 標頭並使用 UTF-8。", ex);
            }
        }

        private static bool HasKnownUtf8Header(byte[] bytes)
        {
            int lineEnd = 0;
            while (lineEnd < bytes.Length && bytes[lineEnd] != 0x0a && bytes[lineEnd] != 0x0d)
            {
                // All supported format headers are plain ASCII. A non-ASCII byte
                // in the first line means this is not one of our known headers.
                if (bytes[lineEnd] < 0x20 || bytes[lineEnd] > 0x7e)
                    return false;
                lineEnd++;
            }

            string firstLine = Encoding.ASCII.GetString(bytes, 0, lineEnd).Trim();
            switch (firstLine)
            {
                case "TMRHIRO-SRP-TEXT/1":
                case "TMRHIRO-SRP-TEXT/2":
                case "TMRHIRO-SRP-TEXT/3":
                case "TMRHIRO-SRP-TEXT/4":
                case "TMRHIRO-VAR-TEXT/1":
                case "TMRHIRO-VAR-TEXT/2":
                case "TMRHIRO-VAR-TEXT/3":
                    return true;
                default:
                    return false;
            }
        }
    }
}
