using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TmrHiroStudio.Models;

namespace TmrHiroStudio.Core
{
    internal static class GrdCodec
    {
        private const int HeaderSize = 0x20;
        // BGRA32 至少需要 160 MB，另有解壓通道暫存；限制避免損壞檔案要求數 GB 配置。
        private const int MaximumPixels = 40000000;

        public static GrdMetadata ReadMetadata(byte[] data)
        {
            if (data == null || data.Length < HeaderSize)
                throw new InvalidDataException("GRD 標頭不足 32 位元組。");

            ushort format = BinaryUtil.ReadUInt16(data, 0);
            int version = format & 0xff;
            int packType = format >> 8;
            if ((version != 1 && version != 2) ||
                (packType != 1 && packType != 0xA1 && packType != 0xA2))
                throw new InvalidDataException("不支援的 GRD 格式標記：0x" + format.ToString("X4"));

            GrdMetadata meta = new GrdMetadata
            {
                Format = format,
                ScreenWidth = BinaryUtil.ReadUInt16(data, 2),
                ScreenHeight = BinaryUtil.ReadUInt16(data, 4),
                Bpp = BinaryUtil.ReadUInt16(data, 6),
                Left = BinaryUtil.ReadUInt16(data, 8),
                Right = BinaryUtil.ReadUInt16(data, 10),
                Top = BinaryUtil.ReadUInt16(data, 12),
                Bottom = BinaryUtil.ReadUInt16(data, 14),
                AlphaSize = BinaryUtil.ReadUInt32(data, 16),
                RSize = BinaryUtil.ReadUInt32(data, 20),
                GSize = BinaryUtil.ReadUInt32(data, 24),
                BSize = BinaryUtil.ReadUInt32(data, 28)
            };

            if (meta.Bpp != 24 && meta.Bpp != 32)
                throw new InvalidDataException("GRD 色彩深度不是 24 或 32 bpp。");
            if (meta.Bpp == 24 && meta.AlphaSize != 0)
                throw new InvalidDataException("24-bit GRD 不應包含 Alpha 通道。");
            if (meta.ScreenWidth == 0 || meta.ScreenHeight == 0)
                throw new InvalidDataException("GRD 畫布尺寸不能為零。");

            int width = meta.Width;
            int height = meta.Height;
            if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels)
                throw new InvalidDataException("GRD 圖像尺寸無效或超出安全上限。");

            ulong expected = (ulong)HeaderSize + meta.AlphaSize + meta.RSize + meta.GSize + meta.BSize;
            if (expected != (ulong)data.Length)
                throw new InvalidDataException("GRD 通道長度與檔案大小不一致。");
            if (meta.RSize == 0 || meta.GSize == 0 || meta.BSize == 0)
                throw new InvalidDataException("GRD 缺少必要的色彩通道。");

            return meta;
        }

        public static bool TryReadMetadata(byte[] data, out GrdMetadata? metadata)
        {
            try
            {
                metadata = ReadMetadata(data);
                return true;
            }
            catch
            {
                metadata = null;
                return false;
            }
        }

        public static void ExtractToPng(string grdPath, string pngPath)
        {
            byte[] data = File.ReadAllBytes(grdPath);
            GrdMetadata meta = ReadMetadata(data);
            byte[] pixels = Decode(data, meta);
            int stride = checked(meta.Width * 4);

            BitmapSource bitmap = BitmapSource.Create(
                meta.Width,
                meta.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            byte[] pngBytes;
            using (MemoryStream stream = new MemoryStream())
            {
                encoder.Save(stream);
                pngBytes = stream.ToArray();
            }

            string sidecar = Path.ChangeExtension(pngPath, ".grd.json");
            WritePngAndSidecarNoOverwrite(pngPath, pngBytes, sidecar, meta);
        }

        public static void CreateFromPng(string pngPath, string grdPath)
        {
            if (File.Exists(grdPath))
                throw new IOException("GRD 輸出檔已存在，為避免覆蓋而停止：" + grdPath);

            int width;
            int height;
            byte[] bgra = ReadPngAsBgra32(pngPath, out width, out height);

            string sidecar = Path.ChangeExtension(pngPath, ".grd.json");
            GrdMetadata meta;
            if (File.Exists(sidecar))
            {
                meta = JsonUtil.Read<GrdMetadata>(sidecar);
                if (meta == null)
                    throw new InvalidDataException("GRD metadata 是空值：" + sidecar);
            }
            else
            {
                meta = new GrdMetadata
                {
                    Format = 0x0101,
                    ScreenWidth = (ushort)width,
                    ScreenHeight = (ushort)height,
                    Bpp = 32,
                    Left = 0,
                    Right = (ushort)width,
                    Top = 0,
                    Bottom = (ushort)height,
                    // 新建 GRD 預設建立 Alpha 通道；1 僅是尚未編碼前的存在旗標。
                    AlphaSize = 1
                };
            }

            if (meta.Version != 1 && meta.Version != 2)
                throw new InvalidDataException("GRD metadata 的版本欄位不正確。");
            if (meta.Bpp != 24 && meta.Bpp != 32)
                throw new InvalidDataException("GRD metadata 的色彩深度不正確。");
            if (meta.Bpp == 24 && meta.AlphaSize != 0)
                throw new InvalidDataException("24-bit GRD metadata 不應包含 Alpha 通道。");

            int right = checked((int)meta.Left + width);
            int bottom = checked((int)meta.Top + height);
            if (right > ushort.MaxValue || bottom > ushort.MaxValue)
                throw new InvalidDataException("PNG 新尺寸超出 GRD 座標範圍。");

            meta.Right = (ushort)right;
            meta.Bottom = (ushort)bottom;
            if (meta.ScreenWidth < meta.Right) meta.ScreenWidth = meta.Right;
            if (meta.ScreenHeight < meta.Bottom) meta.ScreenHeight = meta.Bottom;
            if (meta.ScreenWidth == 0 || meta.ScreenHeight == 0)
                throw new InvalidDataException("畫布尺寸不能為零。");

            bool pngHasTransparency = HasNonOpaqueAlpha(bgra);
            bool sourceHasAlpha = meta.Bpp == 32 && meta.AlphaSize > 0;

            if (meta.Bpp == 24 && pngHasTransparency)
            {
                throw new InvalidDataException(
                    "PNG 含有透明像素，但來源 GRD metadata 是 24bpp，無法儲存 Alpha。" +
                    "請先確認是否要改用 32bpp，再重新建立 GRD；本次未寫入輸出檔。");
            }

            bool writeAlpha = meta.Bpp == 32 && (sourceHasAlpha || pngHasTransparency);
            byte[] alphaData = writeAlpha ? CompressChannel(bgra, width, height, 3) : Array.Empty<byte>();
            byte[] redData = CompressChannel(bgra, width, height, 2);
            byte[] greenData = CompressChannel(bgra, width, height, 1);
            byte[] blueData = CompressChannel(bgra, width, height, 0);

            meta.Format = (ushort)(0x0100 | meta.Version);
            meta.AlphaSize = (uint)alphaData.Length;
            meta.RSize = (uint)redData.Length;
            meta.GSize = (uint)greenData.Length;
            meta.BSize = (uint)blueData.Length;

            byte[] grdBytes = BuildGrdBytes(meta, alphaData, redData, greenData, blueData);
            VerifyRoundTrip(grdBytes, bgra, width, height);
            WriteVerifiedAtomically(grdPath, grdBytes, bgra, width, height, overwrite: false);

            // 不修改來源 PNG 的 .grd.json；該 sidecar 是輸入資產，回封成功不應反向改寫來源。
        }

        private static byte[] ReadPngAsBgra32(string pngPath, out int width, out int height)
        {
            BitmapSource source;
            using (FileStream input = File.OpenRead(pngPath))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(
                    input,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

                if (decoder.Frames.Count == 0)
                    throw new InvalidDataException("PNG 沒有可讀取的影格。");
                source = decoder.Frames[0];
            }

            if (source.PixelWidth <= 0 || source.PixelHeight <= 0 ||
                source.PixelWidth > ushort.MaxValue || source.PixelHeight > ushort.MaxValue)
                throw new InvalidDataException("PNG 尺寸超出 GRD 可表示的範圍。");
            if ((long)source.PixelWidth * source.PixelHeight > MaximumPixels)
                throw new InvalidDataException("PNG 像素數超出安全上限。");

            FormatConvertedBitmap converted = new FormatConvertedBitmap(
                source,
                PixelFormats.Bgra32,
                null,
                0);

            width = converted.PixelWidth;
            height = converted.PixelHeight;
            int stride = checked(width * 4);
            byte[] bgra = new byte[checked(stride * height)];
            converted.CopyPixels(bgra, stride, 0);
            return bgra;
        }

        private static bool HasNonOpaqueAlpha(byte[] bgra)
        {
            for (int i = 3; i < bgra.Length; i += 4)
                if (bgra[i] != 255) return true;
            return false;
        }

        private static byte[] BuildGrdBytes(
            GrdMetadata meta,
            byte[] alphaData,
            byte[] redData,
            byte[] greenData,
            byte[] blueData)
        {
            using (MemoryStream output = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(output))
            {
                writer.Write(meta.Format);
                writer.Write(meta.ScreenWidth);
                writer.Write(meta.ScreenHeight);
                writer.Write(meta.Bpp);
                writer.Write(meta.Left);
                writer.Write(meta.Right);
                writer.Write(meta.Top);
                writer.Write(meta.Bottom);
                writer.Write((uint)alphaData.Length);
                writer.Write((uint)redData.Length);
                writer.Write((uint)greenData.Length);
                writer.Write((uint)blueData.Length);
                if (alphaData.Length > 0) writer.Write(alphaData);
                writer.Write(redData);
                writer.Write(greenData);
                writer.Write(blueData);
                return output.ToArray();
            }
        }

        private static void VerifyRoundTrip(byte[] grdBytes, byte[] expectedBgra, int expectedWidth, int expectedHeight)
        {
            GrdMetadata rebuiltMeta = ReadMetadata(grdBytes);
            if (rebuiltMeta.Width != expectedWidth || rebuiltMeta.Height != expectedHeight)
            {
                throw new InvalidDataException(
                    "GRD 回讀尺寸不一致。PNG=" + expectedWidth + "x" + expectedHeight +
                    "，GRD=" + rebuiltMeta.Width + "x" + rebuiltMeta.Height + "。");
            }

            byte[] decodedBgra = Decode(grdBytes, rebuiltMeta);
            if (decodedBgra.Length != expectedBgra.Length)
                throw new InvalidDataException("GRD 回讀像素緩衝區長度不一致。");

            for (int offset = 0; offset < expectedBgra.Length; offset += 4)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    if (expectedBgra[offset + channel] == decodedBgra[offset + channel])
                        continue;

                    int pixelIndex = offset / 4;
                    int x = pixelIndex % expectedWidth;
                    int y = pixelIndex / expectedWidth;
                    string[] channelNames = { "B", "G", "R", "A" };
                    throw new InvalidDataException(
                        "GRD 像素往返驗證失敗：座標 (" + x + ", " + y + ")，通道 " +
                        channelNames[channel] + "，PNG=" + expectedBgra[offset + channel] +
                        "，GRD=" + decodedBgra[offset + channel] +
                        "。正式輸出檔尚未替換。");
                }
            }
        }

        private static void WritePngAndSidecarNoOverwrite(
            string pngPath,
            byte[] pngBytes,
            string sidecarPath,
            GrdMetadata metadata)
        {
            string fullPng = Path.GetFullPath(pngPath);
            string fullSidecar = Path.GetFullPath(sidecarPath);
            string? directory = Path.GetDirectoryName(fullPng);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("無法判斷 PNG 輸出資料夾。");
            Directory.CreateDirectory(directory);

            if (File.Exists(fullPng) || Directory.Exists(fullPng))
                throw new IOException("PNG 輸出檔已存在，為避免覆蓋而停止：" + fullPng);
            if (File.Exists(fullSidecar) || Directory.Exists(fullSidecar))
                throw new IOException("GRD sidecar 已存在，為避免覆蓋而停止：" + fullSidecar);

            string token = "." + Guid.NewGuid().ToString("N") + ".tmp";
            string tempPng = fullPng + token;
            string tempSidecar = fullSidecar + token;
            bool pngCommitted = false;

            try
            {
                File.WriteAllBytes(tempPng, pngBytes);
                JsonUtil.Write(tempSidecar, metadata);

                if (File.Exists(fullPng) || File.Exists(fullSidecar))
                    throw new IOException("PNG 或 sidecar 在寫入期間已出現，輸出已停止。");

                File.Move(tempPng, fullPng);
                pngCommitted = true;
                File.Move(tempSidecar, fullSidecar);
            }
            catch
            {
                if (pngCommitted)
                {
                    try { if (File.Exists(fullPng)) File.Delete(fullPng); }
                    catch { }
                }
                throw;
            }
            finally
            {
                try { if (File.Exists(tempPng)) File.Delete(tempPng); }
                catch { }
                try { if (File.Exists(tempSidecar)) File.Delete(tempSidecar); }
                catch { }
            }
        }

        private static void WriteVerifiedAtomically(
            string grdPath,
            byte[] grdBytes,
            byte[] expectedBgra,
            int width,
            int height,
            bool overwrite)
        {
            string fullPath = Path.GetFullPath(grdPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("無法判斷 GRD 輸出資料夾。");

            Directory.CreateDirectory(directory);
            if (!overwrite && File.Exists(fullPath))
                throw new IOException("GRD 輸出檔已存在，為避免覆蓋而停止：" + fullPath);

            string tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, grdBytes);
                byte[] writtenBytes = File.ReadAllBytes(tempPath);
                VerifyRoundTrip(writtenBytes, expectedBgra, width, height);

                if (overwrite && File.Exists(fullPath))
                    File.Replace(tempPath, fullPath, null);
                else
                    File.Move(tempPath, fullPath);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch { }
            }
        }

        private static byte[] Decode(byte[] data, GrdMetadata meta)
        {
            int pixelsCount = checked(meta.Width * meta.Height);
            byte[] result = new byte[checked(pixelsCount * 4)];
            int offset = HeaderSize;

            if (meta.Bpp == 32 && meta.AlphaSize > 0)
            {
                byte[] alpha = DecodeChannel(data, offset, checked((int)meta.AlphaSize), pixelsCount, meta.PackType);
                CopyChannel(alpha, result, meta.Width, meta.Height, 3);
                offset += checked((int)meta.AlphaSize);
            }
            else
            {
                for (int i = 3; i < result.Length; i += 4)
                    result[i] = 255;
            }

            byte[] red = DecodeChannel(data, offset, checked((int)meta.RSize), pixelsCount, meta.PackType);
            CopyChannel(red, result, meta.Width, meta.Height, 2);
            offset += checked((int)meta.RSize);

            byte[] green = DecodeChannel(data, offset, checked((int)meta.GSize), pixelsCount, meta.PackType);
            CopyChannel(green, result, meta.Width, meta.Height, 1);
            offset += checked((int)meta.GSize);

            byte[] blue = DecodeChannel(data, offset, checked((int)meta.BSize), pixelsCount, meta.PackType);
            CopyChannel(blue, result, meta.Width, meta.Height, 0);
            return result;
        }

        private static byte[] DecodeChannel(byte[] data, int offset, int size, int outputLength, int packType)
        {
            BinaryUtil.EnsureRange(data.Length, offset, size, "GRD channel");
            byte[] packed = new byte[size];
            Buffer.BlockCopy(data, offset, packed, 0, size);
            if (packType == 1)
                return DecodeRle(packed, outputLength);

            byte[] huffman = DecodeHuffman(packed, outputLength);
            if (packType == 0xA2)
                return DecodeLz77(huffman, outputLength);
            return DecodeRle(huffman, outputLength);
        }

        private static byte[] DecodeRle(byte[] input, int expectedLength)
        {
            byte[] output = new byte[expectedLength];
            int src = 0;
            int dst = 0;
            while (src < input.Length && dst < expectedLength)
            {
                int count = input[src++];
                if ((count & 0x80) != 0)
                {
                    count &= 0x7f;
                    if (count == 0 || src >= input.Length)
                        throw new InvalidDataException("GRD RLE 重複區塊截斷或長度為零。");
                    byte value = input[src++];
                    if (count > expectedLength - dst)
                        throw new InvalidDataException("GRD RLE 輸出超出通道尺寸。");
                    for (int i = 0; i < count; i++)
                        output[dst++] = value;
                }
                else if (count > 0)
                {
                    if (count > input.Length - src || count > expectedLength - dst)
                        throw new InvalidDataException("GRD RLE 文字區塊長度超出範圍。");
                    Buffer.BlockCopy(input, src, output, dst, count);
                    src += count;
                    dst += count;
                }
            }

            if (dst != expectedLength)
                throw new InvalidDataException("GRD RLE 解壓後的通道長度不正確。");
            if (src != input.Length)
                throw new InvalidDataException("GRD RLE 通道末端有未使用的多餘資料。");
            return output;
        }

        private sealed class HuffmanNode
        {
            public uint Frequency;
            public int Left;
            public int Right;
        }

        private static byte[] DecodeHuffman(byte[] input, int expectedPixels)
        {
            const int treeHeader = 8 + 256 * 4;
            if (input.Length < treeHeader)
                throw new InvalidDataException("GRD Huffman 樹資料不足。");

            int unpackedLength = BinaryUtil.ReadInt32(input, 0);
            _ = BinaryUtil.ReadUInt32(input, 4);
            long maxRleLength = (long)expectedPixels * 2 + 16;
            if (unpackedLength <= 0 || unpackedLength > maxRleLength)
                throw new InvalidDataException("GRD Huffman 解壓長度不合理。");

            int bitStart = treeHeader;
            int bitLength = input.Length - bitStart;
            HuffmanNode[] nodes = new HuffmanNode[0x200];
            List<int> tree = new List<int>(0x200);

            for (int i = 0; i < 256; i++)
            {
                uint frequency = BinaryUtil.ReadUInt32(input, 8 + i * 4);
                nodes[i] = new HuffmanNode { Frequency = frequency };
                InsertNode(tree, nodes, i);
            }

            int nextNode = 0x100;
            while (tree.Count > 1)
            {
                int left = tree[0];
                tree.RemoveAt(0);
                int right = tree[0];
                tree.RemoveAt(0);
                if (nextNode >= nodes.Length)
                    throw new InvalidDataException("GRD Huffman 樹節點超出上限。");

                nodes[nextNode] = new HuffmanNode
                {
                    Frequency = unchecked(nodes[left].Frequency + nodes[right].Frequency),
                    Left = left,
                    Right = right
                };
                InsertNode(tree, nodes, nextNode++);
            }

            byte[] output = new byte[unpackedLength];
            int bytePos = bitStart;
            int bitPos = 8;
            for (int dst = 0; dst < output.Length; dst++)
            {
                int node = 0x1fe;
                while (node > 0xff)
                {
                    int bit = ReadLsbBit(input, bitStart, bitLength, ref bytePos, ref bitPos);
                    node = bit != 0 ? nodes[node].Right : nodes[node].Left;
                    if (node < 0 || node >= nodes.Length || nodes[node] == null)
                        throw new InvalidDataException("GRD Huffman 樹指標無效。");
                }
                output[dst] = (byte)node;
            }
            return output;
        }

        private static void InsertNode(List<int> tree, HuffmanNode[] nodes, int index)
        {
            uint frequency = nodes[index].Frequency;
            int position = 0;
            while (position < tree.Count && nodes[tree[position]].Frequency <= frequency)
                position++;
            tree.Insert(position, index);
        }

        private static int ReadLsbBit(byte[] input, int start, int length, ref int bytePos, ref int bitPos)
        {
            if (bitPos == 8)
            {
                if (bytePos >= start + length)
                    throw new InvalidDataException("GRD Huffman 位元資料已截斷。");
                bitPos = 0;
            }

            int value = (input[bytePos] >> bitPos) & 1;
            bitPos++;
            if (bitPos == 8) bytePos++;
            return value;
        }

        private static byte[] DecodeLz77(byte[] input, int expectedLength)
        {
            if (input.Length < 12)
                throw new InvalidDataException("GRD LZ77 標頭不足。");

            byte special = input[8];
            int src = 12;
            int dst = 0;
            byte[] output = new byte[expectedLength];

            while (dst < expectedLength)
            {
                if (src >= input.Length)
                    throw new InvalidDataException("GRD LZ77 資料已截斷。");

                byte value = input[src++];
                if (value != special)
                {
                    output[dst++] = value;
                    continue;
                }

                if (src >= input.Length)
                    throw new InvalidDataException("GRD LZ77 特殊標記不完整。");
                byte distanceByte = input[src++];
                if (distanceByte == special)
                {
                    output[dst++] = distanceByte;
                    continue;
                }

                if (src >= input.Length)
                    throw new InvalidDataException("GRD LZ77 長度欄位不完整。");

                int count = input[src++];
                int distance = distanceByte > special ? distanceByte - 1 : distanceByte;
                if (distance <= 0 || distance > dst || count <= 0 || count > expectedLength - dst)
                    throw new InvalidDataException("GRD LZ77 回溯距離或複製長度無效。");

                for (int i = 0; i < count; i++)
                {
                    output[dst] = output[dst - distance];
                    dst++;
                }
            }

            // 公開格式資料尚未證實末端 padding 的語意；達到宣告輸出長度後停止，
            // 不擅自把可能合法的對齊位元組判成損壞資料。
            return output;
        }

        private static void CopyChannel(byte[] channel, byte[] pixels, int width, int height, int channelOffset)
        {
            for (int destinationY = 0; destinationY < height; destinationY++)
            {
                int sourceY = height - 1 - destinationY;
                int source = sourceY * width;
                int destination = (destinationY * width) * 4 + channelOffset;
                for (int x = 0; x < width; x++)
                {
                    pixels[destination] = channel[source++];
                    destination += 4;
                }
            }
        }

        private static byte[] CompressChannel(byte[] bgra, int width, int height, int channelOffset)
        {
            byte[] bottomUp = new byte[checked(width * height)];
            int destination = 0;
            for (int y = height - 1; y >= 0; y--)
            {
                int source = y * width * 4 + channelOffset;
                for (int x = 0; x < width; x++)
                {
                    bottomUp[destination++] = bgra[source];
                    source += 4;
                }
            }
            return EncodeRle(bottomUp);
        }

        private static byte[] EncodeRle(byte[] input)
        {
            using (MemoryStream output = new MemoryStream())
            {
                int position = 0;
                while (position < input.Length)
                {
                    int run = 1;
                    while (position + run < input.Length &&
                           input[position + run] == input[position] && run < 127)
                        run++;

                    if (run >= 2)
                    {
                        output.WriteByte((byte)(0x80 | run));
                        output.WriteByte(input[position]);
                        position += run;
                        continue;
                    }

                    int literalStart = position;
                    position++;
                    while (position < input.Length && position - literalStart < 127)
                    {
                        int nextRun = 1;
                        while (position + nextRun < input.Length &&
                               input[position + nextRun] == input[position] && nextRun < 127)
                            nextRun++;
                        if (nextRun >= 2) break;
                        position++;
                    }

                    int literalLength = position - literalStart;
                    output.WriteByte((byte)literalLength);
                    output.Write(input, literalStart, literalLength);
                }
                return output.ToArray();
            }
        }
    }
}
