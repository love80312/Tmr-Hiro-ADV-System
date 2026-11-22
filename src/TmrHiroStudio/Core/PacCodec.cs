using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TmrHiroStudio.Models;

namespace TmrHiroStudio.Core
{
    internal static class PacCodec
    {
        private const string ManifestName = ".tmrhiro.pac.json";
        private const int HeaderSize = 7;
        private const long MaximumVarProbeSize = 64L * 1024 * 1024;

        private sealed class PacIndexEntry
        {
            public string DisplayName = string.Empty;
            public byte[] OriginalNameBytes = Array.Empty<byte>();
            public long Offset;
            public uint Size;
        }

        private sealed class PackEntry
        {
            public string Path = string.Empty;
            public byte[] NameBytes = Array.Empty<byte>();
            public byte[]? TransformedData;
            public long Size;
            public long RelativeOffset;
        }

        public static void Extract(string pacPath, string outputDirectory, Action<string>? log = null)
        {
            List<PacIndexEntry> entries;
            int version;
            int originalNameLength;

            using (FileStream input = File.OpenRead(pacPath))
            using (BinaryReader reader = new BinaryReader(input))
            {
                if (input.Length < HeaderSize)
                    throw new InvalidDataException("PAC 檔案短於 7 位元組標頭。");

                short signedCount = reader.ReadInt16();
                int count = signedCount;
                int nameLength = reader.ReadByte();
                uint dataOffset = reader.ReadUInt32();

                if (count <= 0 || count > 32767)
                    throw new InvalidDataException("PAC 檔案數量不合理。");
                if (nameLength <= 0)
                    throw new InvalidDataException("PAC 名稱欄位長度為零。");

                long v1Offset = HeaderSize + (long)count * (nameLength + 8);
                long v2Offset = HeaderSize + (long)count * (nameLength + 12);
                if (dataOffset == v1Offset)
                    version = 1;
                else if (dataOffset == v2Offset)
                    version = 2;
                else
                    throw new InvalidDataException("PAC data_offset 不符合 Version 1／2 索引結構。");

                if (dataOffset > input.Length)
                    throw new InvalidDataException("PAC 資料區起始位置超出檔案範圍。");

                entries = new List<PacIndexEntry>(count);
                originalNameLength = nameLength;

                for (int i = 0; i < count; i++)
                {
                    byte[] nameBytes = reader.ReadBytes(nameLength);
                    if (nameBytes.Length != nameLength)
                        throw new InvalidDataException("PAC 索引名稱資料截斷。");

                    string decodedName = BinaryUtil.ShiftJis.GetString(nameBytes).TrimEnd('\0', ' ');
                    string safeName = BinaryUtil.SafeLeafName(decodedName);
                    long relativeOffset;
                    uint size;

                    if (version == 1)
                    {
                        relativeOffset = reader.ReadUInt32();
                        size = reader.ReadUInt32();
                    }
                    else
                    {
                        relativeOffset = reader.ReadInt64();
                        size = reader.ReadUInt32();
                    }

                    if (relativeOffset < 0)
                        throw new InvalidDataException("PAC 索引包含負數位移。");

                    long absoluteOffset;
                    try
                    {
                        absoluteOffset = checked((long)dataOffset + relativeOffset);
                    }
                    catch (OverflowException)
                    {
                        throw new InvalidDataException("PAC 索引位移溢位。");
                    }

                    if (absoluteOffset < dataOffset || absoluteOffset > input.Length ||
                        size > input.Length - absoluteOffset)
                        throw new InvalidDataException("PAC 資源範圍超出封包檔案。");

                    entries.Add(new PacIndexEntry
                    {
                        DisplayName = safeName,
                        OriginalNameBytes = nameBytes,
                        Offset = absoluteOffset,
                        Size = size
                    });
                }

                if (input.Position != dataOffset)
                    throw new InvalidDataException("PAC 索引讀取位置與 data_offset 不一致。");
            }

            string fullOutput = Path.GetFullPath(outputDirectory);
            if (File.Exists(fullOutput))
                throw new IOException("PAC 輸出路徑目前是檔案：" + fullOutput);
            if (Directory.Exists(fullOutput))
                throw new IOException("輸出資料夾已存在。為避免覆蓋，請選擇新的空白資料夾：" + fullOutput);

            string? parent = Path.GetDirectoryName(fullOutput);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            string normalizedOutput = fullOutput.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(normalizedOutput))
                throw new InvalidDataException("PAC 輸出路徑不能是磁碟根目錄。");
            string staging = normalizedOutput + ".extracting-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);

            try
            {
                PacManifest manifest = new PacManifest
                {
                    FormatVersion = version,
                    NameLength = originalNameLength,
                    SourcePacName = Path.GetFileName(pacPath)
                };
                HashSet<string> outputNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (FileStream source = File.OpenRead(pacPath))
                {
                    foreach (PacIndexEntry entry in entries)
                    {
                        string extension = DetectExtension(source, entry);
                        bool isScript = string.Equals(extension, ".srp", StringComparison.OrdinalIgnoreCase);
                        string fileName = entry.DisplayName + extension;
                        if (!outputNames.Add(fileName))
                            throw new InvalidDataException("PAC 內有重複輸出檔名，為避免互相覆蓋而停止：" + fileName);

                        string outputPath = Path.Combine(staging, fileName);
                        source.Position = entry.Offset;
                        if (isScript)
                        {
                            if (entry.Size > int.MaxValue)
                                throw new InvalidDataException("SRP 資源超過單一記憶體區塊可處理的大小。");

                            byte[] raw = new byte[(int)entry.Size];
                            ReadExactly(source, raw, 0, raw.Length);
                            byte[] decoded = ScriptCodec.TransformPacScriptLayer(raw);
                            using (FileStream output = new FileStream(
                                outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                output.Write(decoded, 0, decoded.Length);
                            }
                        }
                        else
                        {
                            using (FileStream output = new FileStream(
                                outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                CopyExactly(source, output, entry.Size);
                            }
                        }

                        manifest.Entries.Add(new PacManifestEntry
                        {
                            FileName = fileName,
                            OriginalNameBytes = Convert.ToBase64String(entry.OriginalNameBytes),
                            Extension = extension,
                            IsScriptResource = isScript
                        });
                        log?.Invoke("解包：" + fileName + "（" + entry.Size.ToString("N0") + " bytes）");
                    }
                }

                JsonUtil.Write(Path.Combine(staging, ManifestName), manifest);
                // staging 與正式資料夾位於同一磁碟；成功後以資料夾 rename 一次提交。
                Directory.Move(staging, fullOutput);
                log?.Invoke("PAC 解包完成：Version " + version + "，共 " + entries.Count + " 個資源。");
            }
            catch
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                catch { }
                throw;
            }
        }

        public static void Pack(string inputDirectory, string outputPacPath, int requestedVersion, Action<string>? log = null)
        {
            string fullInput = Path.GetFullPath(inputDirectory);
            if (!Directory.Exists(fullInput))
                throw new DirectoryNotFoundException("找不到回封資料夾：" + fullInput);

            string fullOutput = Path.GetFullPath(outputPacPath);
            if (Directory.Exists(fullOutput))
                throw new IOException("PAC 輸出路徑目前是資料夾：" + fullOutput);
            string fullTempOutput = fullOutput + "." + Guid.NewGuid().ToString("N") + ".tmp";

            if (Directory.EnumerateDirectories(fullInput).Any())
                log?.Invoke("注意：PAC 不儲存目錄階層，回封時只處理資料夾第一層檔案；子資料夾將略過。");

            string manifestPath = Path.Combine(fullInput, ManifestName);
            PacManifest? manifest = File.Exists(manifestPath) ? JsonUtil.Read<PacManifest>(manifestPath) : null;
            int version = (requestedVersion == 1 || requestedVersion == 2)
                ? requestedVersion
                : (manifest != null ? manifest.FormatVersion : 0);
            if (version != 1 && version != 2)
                throw new InvalidDataException("沒有可用的 PAC 版本。請使用 manifest 或明確選擇 Version 1／2。");

            List<PackEntry> packEntries = new List<PackEntry>();
            HashSet<string> addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> uniqueNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> windowsResourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> manifestFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (manifest != null)
            {
                if (manifest.Entries == null)
                    throw new InvalidDataException("PAC manifest 缺少 Entries 清單。");
                if (manifest.NameLength < 1 || manifest.NameLength > 255)
                    throw new InvalidDataException("PAC manifest 的名稱欄位長度無效。");

                foreach (PacManifestEntry original in manifest.Entries)
                {
                    if (original == null)
                        throw new InvalidDataException("PAC manifest 含有空白索引項目。");
                    if (string.IsNullOrWhiteSpace(original.FileName) ||
                        Path.GetFileName(original.FileName) != original.FileName ||
                        original.FileName == "." || original.FileName == ".." ||
                        !string.Equals(original.FileName, BinaryUtil.SafeLeafName(original.FileName), StringComparison.Ordinal))
                        throw new InvalidDataException("PAC manifest 含有不安全的檔名：" + original.FileName);
                    if (!manifestFileNames.Add(original.FileName))
                        throw new InvalidDataException("PAC manifest 含有重複的資源檔名：" + original.FileName);

                    string filePath = Path.Combine(fullInput, original.FileName);
                    string fullFilePath = Path.GetFullPath(filePath);
                    if (!IsInsideDirectory(fullInput, fullFilePath))
                        throw new InvalidDataException("PAC manifest 資源路徑超出回封資料夾：" + original.FileName);
                    if (string.Equals(fullFilePath, fullOutput, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(fullFilePath, fullTempOutput, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("PAC 輸出路徑與 manifest 中的資源檔案衝突，請改選其他輸出位置。");
                    if (!File.Exists(filePath))
                        continue; // 刪除解包檔案即代表不放回封包。

                    byte[] nameBytes;
                    try
                    {
                        nameBytes = Convert.FromBase64String(original.OriginalNameBytes);
                    }
                    catch (Exception ex) when (ex is FormatException || ex is ArgumentNullException)
                    {
                        throw new InvalidDataException("PAC manifest 的原始名稱位元組不是有效 Base64：" + original.FileName);
                    }

                    if (nameBytes.Length != manifest.NameLength)
                        throw new InvalidDataException("PAC manifest 名稱長度與原始索引不一致：" + original.FileName);

                    string nameKey = Convert.ToBase64String(nameBytes);
                    if (!uniqueNames.Add(nameKey))
                        throw new InvalidDataException("PAC manifest 內有重複的原始索引名稱位元組。");
                    string decodedResourceName = BinaryUtil.SafeLeafName(
                        BinaryUtil.ShiftJis.GetString(nameBytes).TrimEnd('\0', ' '));
                    if (!windowsResourceNames.Add(decodedResourceName))
                        throw new InvalidDataException("PAC manifest 內有 Windows 不區分大小寫的同名資源：" + decodedResourceName);

                    PackEntry entry = CreatePackEntry(
                        filePath,
                        nameBytes,
                        original.IsScriptResource ||
                        string.Equals(original.Extension, ".srp", StringComparison.OrdinalIgnoreCase));
                    packEntries.Add(entry);
                    addedPaths.Add(Path.GetFullPath(filePath));
                }
            }

            foreach (string filePath in Directory.EnumerateFiles(fullInput, "*", SearchOption.TopDirectoryOnly)
                         .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(filePath);
                if (string.Equals(fileName, ManifestName, StringComparison.OrdinalIgnoreCase)) continue;
                if (fileName.EndsWith(".grd.json", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".srp.txt", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".var.txt", StringComparison.OrdinalIgnoreCase)) continue;

                string fullFilePath = Path.GetFullPath(filePath);
                if (string.Equals(fullFilePath, fullOutput, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fullFilePath, fullTempOutput, StringComparison.OrdinalIgnoreCase)) continue;
                if (addedPaths.Contains(fullFilePath)) continue;

                string baseName = GetPacBaseName(fileName);
                if (string.IsNullOrWhiteSpace(baseName))
                    throw new InvalidDataException("無法從檔名取得 PAC 資源名稱：" + fileName);

                byte[] nameBytes;
                try
                {
                    nameBytes = BinaryUtil.ShiftJisStrict.GetBytes(baseName);
                }
                catch (EncoderFallbackException)
                {
                    throw new InvalidDataException("PAC 資源名稱無法使用 Shift-JIS 編碼：" + baseName);
                }

                string nameKey = Convert.ToBase64String(nameBytes);
                if (!uniqueNames.Add(nameKey) || !windowsResourceNames.Add(BinaryUtil.SafeLeafName(baseName)))
                    throw new InvalidDataException("PAC 不能含有重複（Windows 不區分大小寫）的資源名稱：" + baseName);

                bool isScript = string.Equals(Path.GetExtension(fileName), ".srp", StringComparison.OrdinalIgnoreCase);
                packEntries.Add(CreatePackEntry(filePath, nameBytes, isScript));
            }

            if (packEntries.Count == 0)
                throw new InvalidDataException("回封資料夾沒有可加入 PAC 的檔案。");
            if (packEntries.Count > 32767)
                throw new InvalidDataException("PAC 檔案數量不可超過 32767。");

            int nameLength = Math.Max(manifest == null ? 0 : manifest.NameLength,
                packEntries.Max(entry => entry.NameBytes.Length));
            if (nameLength <= 0 || nameLength > 255)
                throw new InvalidDataException("PAC 檔名 Shift-JIS 位元組長度必須介於 1～255。");

            long dataOffsetLong = HeaderSize + (long)packEntries.Count * (nameLength + (version == 1 ? 8 : 12));
            if (dataOffsetLong > uint.MaxValue)
                throw new InvalidDataException("PAC 索引區超出 32 位元組 data_offset 可表示範圍。");

            long runningOffset = 0;
            foreach (PackEntry entry in packEntries)
            {
                entry.RelativeOffset = runningOffset;
                runningOffset = checked(runningOffset + entry.Size);
                if (entry.Size > uint.MaxValue)
                    throw new InvalidDataException("單一 PAC 資源超過 4 GiB：" + Path.GetFileName(entry.Path));
                if (version == 1 && entry.RelativeOffset > uint.MaxValue)
                    throw new InvalidDataException("PAC Version 1 的相對位移超過 UInt32，請選擇 Version 2。");
            }

            string? outputDirectory = Path.GetDirectoryName(fullOutput);
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            try
            {
                using (FileStream output = new FileStream(
                    fullTempOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (BinaryWriter writer = new BinaryWriter(output))
                {
                    writer.Write((short)packEntries.Count);
                    writer.Write((byte)nameLength);
                    writer.Write((uint)dataOffsetLong);

                    foreach (PackEntry entry in packEntries)
                    {
                        byte[] fixedName = new byte[nameLength];
                        Buffer.BlockCopy(entry.NameBytes, 0, fixedName, 0, entry.NameBytes.Length);
                        writer.Write(fixedName);
                        if (version == 1) writer.Write((uint)entry.RelativeOffset);
                        else writer.Write(entry.RelativeOffset);
                        writer.Write((uint)entry.Size);
                    }

                    foreach (PackEntry entry in packEntries)
                    {
                        if (entry.TransformedData != null)
                        {
                            writer.Write(entry.TransformedData);
                        }
                        else
                        {
                            using (FileStream input = File.OpenRead(entry.Path))
                            {
                                if (input.Length != entry.Size)
                                    throw new IOException("資源在建立索引後大小已變更，已停止回封：" + entry.Path);
                                CopyExactly(input, output, entry.Size);
                                if (input.Position != input.Length)
                                    throw new IOException("資源讀取位置異常，已停止回封：" + entry.Path);
                            }
                        }
                        log?.Invoke("加入：" + Path.GetFileName(entry.Path));
                    }
                    writer.Flush();
                    output.Flush(true);
                }

                if (File.Exists(fullOutput))
                    File.Replace(fullTempOutput, fullOutput, null);
                else
                    File.Move(fullTempOutput, fullOutput);

                log?.Invoke("PAC 回封完成：Version " + version + "，共 " + packEntries.Count + " 個資源。");
            }
            finally
            {
                try
                {
                    if (File.Exists(fullTempOutput)) File.Delete(fullTempOutput);
                }
                catch { }
            }
        }

        private static PackEntry CreatePackEntry(string filePath, byte[] nameBytes, bool isScript)
        {
            FileInfo info = new FileInfo(filePath);
            if (!info.Exists)
                throw new FileNotFoundException("找不到 PAC 資源檔案。", filePath);

            PackEntry entry = new PackEntry
            {
                Path = filePath,
                NameBytes = nameBytes,
                Size = info.Length
            };

            if (isScript)
            {
                if (info.Length > int.MaxValue)
                    throw new InvalidDataException("SRP 超過單一記憶體區塊可處理的大小：" + filePath);
                byte[] raw = File.ReadAllBytes(filePath);
                if (raw.LongLength != info.Length)
                    throw new IOException("SRP 在讀取期間大小已變更：" + filePath);
                entry.TransformedData = ScriptCodec.TransformPacScriptLayer(raw);
                entry.Size = entry.TransformedData.LongLength;
            }

            return entry;
        }

        private static string DetectExtension(FileStream source, PacIndexEntry entry)
        {
            if (entry.Size == 0) return ".bin";

            byte[] head = new byte[(int)Math.Min(entry.Size, 32)];
            source.Position = entry.Offset;
            ReadExactly(source, head, 0, head.Length);

            if (head.Length >= 4 && head[0] == 0x4f && head[1] == 0x67 &&
                head[2] == 0x67 && head[3] == 0x53)
                return ".ogg";

            if (entry.Size >= 32 && head.Length == 32)
            {
                try
                {
                    ushort format = BinaryUtil.ReadUInt16(head, 0);
                    int version = format & 0xff;
                    int packType = format >> 8;
                    ushort bpp = BinaryUtil.ReadUInt16(head, 6);
                    ulong expected = 32UL + BinaryUtil.ReadUInt32(head, 16) +
                        BinaryUtil.ReadUInt32(head, 20) + BinaryUtil.ReadUInt32(head, 24) +
                        BinaryUtil.ReadUInt32(head, 28);

                    if ((version == 1 || version == 2) &&
                        (packType == 1 || packType == 0xA1 || packType == 0xA2) &&
                        (bpp == 24 || bpp == 32) && expected == entry.Size &&
                        BinaryUtil.ReadUInt16(head, 8) != BinaryUtil.ReadUInt16(head, 10) &&
                        BinaryUtil.ReadUInt16(head, 12) != BinaryUtil.ReadUInt16(head, 14))
                        return ".grd";
                }
                catch { }
            }

            if (entry.Size >= 10 && ScriptCodec.HasPacSrpSignature(head) && entry.Size <= 256L * 1024L * 1024L)
            {
                // 不再只憑 32-byte header sample 將資源判為 SRP。
                // 必須讀取完整資源並通過每筆指令長度、footer 與邊界驗證。
                byte[] candidate = new byte[(int)entry.Size];
                source.Position = entry.Offset;
                ReadExactly(source, candidate, 0, candidate.Length);
                if (ScriptCodec.LooksLikePacSrp(candidate))
                    return ".srp";
            }
            if (entry.Size >= 9 && head.Length >= 9 && head[0] == 0x44 &&
                BinaryUtil.ReadUInt32(head, 5) == entry.Size - 9)
                return ".twav";

            if (entry.Size >= 7 && entry.Size <= MaximumVarProbeSize)
            {
                byte[] tail = new byte[5];
                source.Position = entry.Offset + entry.Size - 5;
                ReadExactly(source, tail, 0, 5);
                if (tail[0] == (byte)'[' && tail[1] == (byte)'E' && tail[2] == (byte)'N' &&
                    tail[3] == (byte)'D' && tail[4] == (byte)']')
                {
                    byte[] candidate = new byte[(int)entry.Size];
                    source.Position = entry.Offset;
                    ReadExactly(source, candidate, 0, candidate.Length);
                    if (ScriptTextCodec.IsValidVarBinary(candidate))
                        return ".var";
                }
            }

            return ".bin";
        }

        private static string GetPacBaseName(string fileName)
        {
            string[] known = { ".grd", ".srp", ".ogg", ".twav", ".var", ".bin" };
            foreach (string extension in known)
            {
                if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return fileName.Substring(0, fileName.Length - extension.Length);
            }
            return fileName;
        }

        private static bool IsInsideDirectory(string root, string candidate)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullCandidate = Path.GetFullPath(candidate);
            return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static void ReadExactly(Stream input, byte[] buffer, int offset, int count)
        {
            int readTotal = 0;
            while (readTotal < count)
            {
                int read = input.Read(buffer, offset + readTotal, count - readTotal);
                if (read <= 0)
                    throw new EndOfStreamException("PAC 資源資料意外結束。");
                readTotal += read;
            }
        }

        private static void CopyExactly(Stream input, Stream output, long size)
        {
            byte[] buffer = new byte[1024 * 1024];
            long remaining = size;
            while (remaining > 0)
            {
                int wanted = (int)Math.Min(buffer.Length, remaining);
                int read = input.Read(buffer, 0, wanted);
                if (read <= 0)
                    throw new EndOfStreamException("PAC 資源資料意外結束。");
                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }
    }
}
