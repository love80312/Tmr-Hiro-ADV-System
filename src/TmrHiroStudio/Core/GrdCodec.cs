using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TmrHiroStudio.Models;

namespace TmrHiroStudio.Core
{
    internal static class GrdCodec
    {
        private const int HeaderSize = 0x20;
        private const int MaximumPixels = 100000000;

        public static GrdMetadata ReadMetadata(byte[] data)
        {
            if (data == null || data.Length < HeaderSize) throw new InvalidDataException("GRD 標頭不足 32 位元組。");
            ushort format = BinaryUtil.ReadUInt16(data, 0);
            int version = format & 0xff;
            int packType = format >> 8;
            if ((version != 1 && version != 2) || (packType != 1 && packType != 0xA1 && packType != 0xA2))
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

            if (meta.Bpp != 24 && meta.Bpp != 32) throw new InvalidDataException("GRD 色彩深度不是 24 或 32 bpp。");
            if (meta.Bpp == 24 && meta.AlphaSize != 0) throw new InvalidDataException("24-bit GRD 不應包含 Alpha 通道。");
            int width = meta.Width;
            int height = meta.Height;
            if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels)
                throw new InvalidDataException("GRD 圖像尺寸無效或超出安全上限。");

            ulong expected = (ulong)HeaderSize + meta.AlphaSize + meta.RSize + meta.GSize + meta.BSize;
            if (expected != (ulong)data.Length) throw new InvalidDataException("GRD 通道長度與檔案大小不一致。");
            if (meta.RSize == 0 || meta.GSize == 0 || meta.BSize == 0) throw new InvalidDataException("GRD 缺少必要的色彩通道。");
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
            int stride = meta.Width * 4;
            BitmapSource bitmap = BitmapSource.Create(meta.Width, meta.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            byte[] pngBytes;
            using (MemoryStream stream = new MemoryStream())
            {
                encoder.Save(stream);
                pngBytes = stream.ToArray();
            }
            BinaryUtil.WriteAtomically(pngPath, pngBytes);
            string sidecar = Path.ChangeExtension(pngPath, ".grd.json");
            JsonUtil.Write(sidecar, meta);
        }

        public static void CreateFromPng(string pngPath, string grdPath)
        {
            BitmapSource source;
            using (FileStream input = File.OpenRead(pngPath))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) throw new InvalidDataException("PNG 沒有可讀取的影格。");
                source = decoder.Frames[0];
            }
            if (source.PixelWidth <= 0 || source.PixelHeight <= 0 || source.PixelWidth > 65535 || source.PixelHeight > 65535)
                throw new InvalidDataException("PNG 尺寸超出 GRD 可表示的範圍。");
            if ((long)source.PixelWidth * source.PixelHeight > MaximumPixels)
                throw new InvalidDataException("PNG 像素數超出安全上限。");

            FormatConvertedBitmap converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = checked(width * 4);
            byte[] bgra = new byte[checked(stride * height)];
            converted.CopyPixels(bgra, stride, 0);

            string sidecar = Path.ChangeExtension(pngPath, ".grd.json");
            GrdMetadata meta;
            if (File.Exists(sidecar))
            {
                meta = JsonUtil.Read<GrdMetadata>(sidecar);
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
                    AlphaSize = 1
                };
            }

            if (meta.Version != 1 && meta.Version != 2) throw new InvalidDataException("GRD metadata 的版本欄位不正確。");
            if (meta.Bpp != 24 && meta.Bpp != 32) throw new InvalidDataException("GRD metadata 的色彩深度不正確。");
            if (meta.Bpp == 24 && meta.AlphaSize != 0) throw new InvalidDataException("24-bit GRD metadata 不應包含 Alpha 通道。");
            int right = (int)meta.Left + width;
            int bottom = (int)meta.Top + height;
            if (right > 65535 || bottom > 65535) throw new InvalidDataException("PNG 新尺寸超出 GRD 座標範圍。");
            meta.Right = (ushort)right;
            meta.Bottom = (ushort)bottom;
            if (meta.ScreenWidth < meta.Right) meta.ScreenWidth = meta.Right;
            if (meta.ScreenHeight < meta.Bottom) meta.ScreenHeight = meta.Bottom;
            if (meta.ScreenWidth == 0 || meta.ScreenHeight == 0) throw new InvalidDataException("畫布尺寸不能為零。");

            bool alpha = meta.Bpp == 32 && meta.AlphaSize > 0;
            byte[] alphaData = alpha ? CompressChannel(bgra, width, height, 3) : Array.Empty<byte>();
            byte[] redData = CompressChannel(bgra, width, height, 2);
            byte[] greenData = CompressChannel(bgra, width, height, 1);
            byte[] blueData = CompressChannel(bgra, width, height, 0);

            using (MemoryStream output = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(output))
            {
                // GRD 的壓縮類型統一採用已知可逆的 RLE (0x01)，保留原始版本低位元組。
                ushort format = (ushort)(0x0100 | meta.Version);
                writer.Write(format);
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
                BinaryUtil.WriteAtomically(grdPath, output.ToArray());
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
                for (int i = 3; i < result.Length; i += 4) result[i] = 255;
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
            if (packType == 1) return DecodeRle(packed, outputLength);

            byte[] huffman = DecodeHuffman(packed, outputLength);
            if (packType == 0xA2) return DecodeLz77(huffman, outputLength);
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
                    if (count == 0 || src >= input.Length) throw new InvalidDataException("GRD RLE 重複區塊截斷或長度為零。");
                    byte value = input[src++];
                    if (count > expectedLength - dst) throw new InvalidDataException("GRD RLE 輸出超出通道尺寸。");
                    for (int i = 0; i < count; i++) output[dst++] = value;
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
            if (dst != expectedLength) throw new InvalidDataException("GRD RLE 解壓後的通道長度不正確。");
            if (src != input.Length) throw new InvalidDataException("GRD RLE 通道末端有未使用的多餘資料。");
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
            if (input.Length < treeHeader) throw new InvalidDataException("GRD Huffman 樹資料不足。");
            int unpackedLength = BinaryUtil.ReadInt32(input, 0);
            // 此欄位在已公開的 Tmr-Hiro 解碼器中未被用來裁切 bit stream；
            // 以目前 GRD 通道的實際長度作為唯一邊界，避免不同遊戲對 packed_size 的定義差異。
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
                int left = tree[0]; tree.RemoveAt(0);
                int right = tree[0]; tree.RemoveAt(0);
                if (nextNode >= nodes.Length) throw new InvalidDataException("GRD Huffman 樹節點超出上限。");
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
                    if (node < 0 || node >= nodes.Length) throw new InvalidDataException("GRD Huffman 樹指標無效。");
                }
                output[dst] = (byte)node;
            }
            return output;
        }

        private static void InsertNode(List<int> tree, HuffmanNode[] nodes, int index)
        {
            uint frequency = nodes[index].Frequency;
            int position = 0;
            while (position < tree.Count && nodes[tree[position]].Frequency <= frequency) position++;
            tree.Insert(position, index);
        }

        private static int ReadLsbBit(byte[] input, int start, int length, ref int bytePos, ref int bitPos)
        {
            if (bitPos == 8)
            {
                if (bytePos >= start + length) throw new InvalidDataException("GRD Huffman 位元資料已截斷。");
                bitPos = 0;
            }
            int value = (input[bytePos] >> bitPos) & 1;
            bitPos++;
            if (bitPos == 8) bytePos++;
            return value;
        }

        private static byte[] DecodeLz77(byte[] input, int expectedLength)
        {
            if (input.Length < 12) throw new InvalidDataException("GRD LZ77 標頭不足。");
            byte special = input[8];
            int src = 12;
            int dst = 0;
            byte[] output = new byte[expectedLength];
            while (dst < expectedLength)
            {
                if (src >= input.Length) throw new InvalidDataException("GRD LZ77 資料已截斷。");
                byte value = input[src++];
                if (value != special)
                {
                    output[dst++] = value;
                    continue;
                }
                if (src >= input.Length) throw new InvalidDataException("GRD LZ77 特殊標記不完整。");
                byte distanceByte = input[src++];
                if (distanceByte == special)
                {
                    output[dst++] = distanceByte;
                    continue;
                }
                if (src >= input.Length) throw new InvalidDataException("GRD LZ77 長度欄位不完整。");
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
                    while (position + run < input.Length && input[position + run] == input[position] && run < 127) run++;
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
                        while (position + nextRun < input.Length && input[position + nextRun] == input[position] && nextRun < 127) nextRun++;
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
