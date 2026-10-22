using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace QuickLook.Plugin.XisfViewer
{
    public sealed class PreviewImage
    {
        public int Width, Height;
        public byte[] Bgr;
        public string Info;
    }

    /// <summary>
    /// Minimal, dependency-free monolithic XISF 1.0 reader aimed at previews.
    /// Supported: attachment / inline / embedded; zlib / lz4 / lz4hc / zstd / byte shuffle;
    /// UInt8/16/32, Int16/32, Float32/64, Gray/RGB, planar/normal, common 2x2 CFAs.
    /// Deliberately does not interpret WCS or distributed XISF or tables.
    /// </summary>
    public static class XisfDecoder
    {
        private const int MaximumHeaderBytes = 32 * 1024 * 1024;
        private const long MaximumPixelBytes = 1000L * 1024L * 1024L;

        // QuickLook can call Preview(path); Explorer provides only an IStream.
        // The shared decoder therefore supports both a filename and a seekable stream.
        public static PreviewImage Preview(string filePath, CancellationToken cancel, int maxEdge = 1800)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return Preview(fs, cancel, maxEdge);
        }

        public static PreviewImage Preview(Stream fs, CancellationToken cancel, int maxEdge = 512)
        {
            if (fs == null || !fs.CanRead || !fs.CanSeek)
                throw new InvalidDataException("XISF: wymagany strumien z odczytem i przewijaniem");
            if (maxEdge < 16 || maxEdge > 1800)
                throw new ArgumentOutOfRangeException(nameof(maxEdge));
            fs.Position = 0;
                var preamble = ReadExactly(fs, 16);
                if (Encoding.ASCII.GetString(preamble, 0, 8) != "XISF0100")
                    throw new InvalidDataException("To nie jest plik XISF 1.0");
                var headerLen = BitConverter.ToUInt32(preamble, 8);
                if (headerLen == 0 || headerLen > MaximumHeaderBytes || 16L + headerLen > fs.Length)
                    throw new InvalidDataException("Nieprawidłowa długość nagłówka XISF");

                var xmlBytes = ReadExactly(fs, checked((int)headerLen));
                var document = XDocument.Parse(Encoding.UTF8.GetString(xmlBytes));
                var image = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Image");
                if (image == null)
                    throw new NotSupportedException("XISF nie zawiera obrazu");
                var geometry = Attribute(image, "geometry").Split(':');
                if (geometry.Length != 3)
                    throw new NotSupportedException("Obsługiwane są obrazy 2D z 1 lub 3 kanałami");
                int width = PositiveInt(geometry[0]), height = PositiveInt(geometry[1]);
                int channels = PositiveInt(geometry[2]);
                if (channels != 1 && channels != 3)
                    throw new NotSupportedException("Obsługiwane są obrazy mono i RGB");

                string format = Attribute(image, "sampleFormat");
                int sampleSize = BytesPerSample(format);
                long pixels = checked((long)width * height);
                long size = checked(pixels * channels * sampleSize);
                if (size > MaximumPixelBytes) throw new NotSupportedException("Obraz za duży dla wersji testowej");
                cancel.ThrowIfCancellationRequested();

                string compression = (string)image.Attribute("compression") ?? "";
                var raw = ReadImageBlock(fs, image, size, cancel);
                bool bigEndian = string.Equals((string)image.Attribute("byteOrder"), "big", StringComparison.OrdinalIgnoreCase);
                bool normal = string.Equals((string)image.Attribute("pixelStorage"), "normal", StringComparison.OrdinalIgnoreCase);
                string cfa = image.Descendants().FirstOrDefault(e => e.Name.LocalName == "ColorFilterArray")?.Attribute("pattern")?.Value;
                if (cfa != null) cfa = cfa.Trim().ToUpperInvariant();
                if (cfa != "RGGB" && cfa != "BGGR" && cfa != "GRBG" && cfa != "GBRG") cfa = null;
                if (channels != 1) cfa = null;

                var imageData = new PixelData(raw, width, height, channels, format, sampleSize, bigEndian, normal);
                var stretch = Stretch.Compute(imageData, cancel);
                double resize = Math.Min(1.0, maxEdge / (double)Math.Max(width, height));
                int outW = Math.Max(1, (int)Math.Round(width * resize));
                int outH = Math.Max(1, (int)Math.Round(height * resize));
                var bgr = new byte[checked(outW * outH * 3)];

                for (int y = 0; y < outH; y++)
                {
                    if ((y & 31) == 0) cancel.ThrowIfCancellationRequested();
                    int sy = Math.Min(height - 1, (int)((y + 0.5) * height / outH));
                    for (int x = 0; x < outW; x++)
                    {
                        int sx = Math.Min(width - 1, (int)((x + 0.5) * width / outW));
                        int index = (y * outW + x) * 3;
                        if (cfa != null)
                        {
                            bgr[index] = stretch.Map(Bayer(imageData, sx, sy, cfa, 2));
                            bgr[index + 1] = stretch.Map(Bayer(imageData, sx, sy, cfa, 1));
                            bgr[index + 2] = stretch.Map(Bayer(imageData, sx, sy, cfa, 0));
                        }
                        else
                        {
                            bgr[index] = stretch.Map(imageData.Sample(sx, sy, Math.Min(2, channels - 1)));
                            bgr[index + 1] = stretch.Map(imageData.Sample(sx, sy, Math.Min(1, channels - 1)));
                            bgr[index + 2] = stretch.Map(imageData.Sample(sx, sy, 0));
                        }
                    }
                }

                return new PreviewImage
                {
                    Width = outW,
                    Height = outH,
                    Bgr = bgr,
                    Info = $"XISF  {width} × {height}  |  {format}  |  {(cfa != null ? "CFA " + cfa : channels == 3 ? "RGB" : "MONO")}  |  {(compression.Length == 0 ? "bez kompresji" : compression.Split(':')[0])}  |  AutoSTF (podgląd, plik bez zmian)"
                };
        }

        private static double Bayer(PixelData data, int x, int y, string bayer, int target)
        {
            int centerColor = BayerAt(x, y, bayer);
            if (centerColor == target) return data.Sample(x, y, 0);
            double sum = 0;
            int count = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = x + dx, cy = y + dy;
                    if (cx < 0 || cy < 0 || cx >= data.Width || cy >= data.Height) continue;
                    if (BayerAt(cx, cy, bayer) == target)
                    {
                        sum += data.Sample(cx, cy, 0);
                        count++;
                    }
                }
            return count > 0 ? sum / count : data.Sample(x, y, 0);
        }

        private static int BayerAt(int x, int y, string bayer)
        {
            char c = bayer[((y & 1) << 1) + (x & 1)];
            return c == 'R' ? 0 : c == 'G' ? 1 : 2;
        }

        private static string Attribute(XElement e, string name) =>
            (string)e.Attribute(name) ?? throw new InvalidDataException("Brak atrybutu XISF: " + name);

        private static int PositiveInt(string s)
        {
            int i = int.Parse(s, CultureInfo.InvariantCulture);
            if (i < 1) throw new InvalidDataException("Nieprawidłowy rozmiar obrazu");
            return i;
        }

        private static int BytesPerSample(string f)
        {
            switch (f)
            {
                case "UInt8": return 1;
                case "UInt16": case "Int16": return 2;
                case "UInt32": case "Int32": case "Float32": return 4;
                case "Float64": return 8;
                default: throw new NotSupportedException("Nieobsługiwany sampleFormat: " + f);
            }
        }

        private static byte[] ReadImageBlock(Stream fs, XElement element, long expectedBytes, CancellationToken cancel)
        {
            string location = Attribute(element, "location");
            byte[] compressed;
            if (location.StartsWith("attachment:", StringComparison.Ordinal))
            {
                var locationParts = location.Split(':');
                if (locationParts.Length != 3) throw new InvalidDataException("Błędny attachment");
                long offset = long.Parse(locationParts[1], CultureInfo.InvariantCulture);
                long bytes = long.Parse(locationParts[2], CultureInfo.InvariantCulture);
                if (offset < 16 || bytes < 0 || bytes > MaximumPixelBytes || offset > fs.Length - bytes)
                    throw new InvalidDataException("Attachment poza granicami pliku");
                fs.Position = offset;
                compressed = ReadExactly(fs, checked((int)bytes));
            }
            else if (location.StartsWith("inline:", StringComparison.Ordinal))
            {
                compressed = DecodeInline(element.Value, location.Substring(7));
            }
            else if (location == "embedded")
            {
                var data = element.Elements().FirstOrDefault(e => e.Name.LocalName == "Data");
                if (data == null) throw new InvalidDataException("Brak elementu Data");
                compressed = DecodeInline(data.Value, (string)data.Attribute("encoding") ?? "base64");
                element = data;
            }
            else throw new NotSupportedException("Nieobsługiwane położenie bloku: " + location);

            string compression = (string)element.Attribute("compression") ?? "";
            if (compression.Length == 0)
            {
                if (compressed.LongLength != expectedBytes) throw new InvalidDataException("Niezgodna długość nieskompresowanego obrazu");
                return compressed;
            }

            var parts = compression.Split(':');
            if (parts.Length < 2) throw new InvalidDataException("Błędny atrybut compression");
            string codec = parts[0].ToLowerInvariant();
            int unpacked = checked((int)long.Parse(parts[1], CultureInfo.InvariantCulture));
            if (unpacked != expectedBytes) throw new InvalidDataException("Niezgodna długość po dekompresji");
            int shuffleSize = codec.EndsWith("+sh", StringComparison.Ordinal) && parts.Length >= 3
                ? PositiveInt(parts[2]) : 1;
            codec = codec.Replace("+sh", "");
            var blocks = new List<Tuple<int, int>>();
            string subblocks = (string)element.Attribute("subblocks") ?? "";
            if (subblocks.Length > 0)
            {
                foreach (var item in subblocks.Split(':'))
                {
                    var lengths = item.Split(',');
                    if (lengths.Length != 2) throw new InvalidDataException("Błędne subblocks");
                    blocks.Add(Tuple.Create(PositiveInt(lengths[0]), PositiveInt(lengths[1])));
                }
            }
            else blocks.Add(Tuple.Create(compressed.Length, unpacked));

            if (codec != "zlib" && codec != "lz4" && codec != "lz4hc" && codec != "zstd")
                throw new NotSupportedException("Nieobsługiwany algorytm kompresji: " + codec);

            var dest = new byte[unpacked];
            int sourceOffset = 0, targetOffset = 0;
            // A decompressor is reused across XISF subblocks. ZstdSharp.Port is pure C#;
            // no x86/x64-specific native DLL is required.
            using (var zstd = codec == "zstd" ? new ZstdSharp.Decompressor() : null)
            {
                foreach (var block in blocks)
                {
                    cancel.ThrowIfCancellationRequested();
                    int inputSize = block.Item1, outputSize = block.Item2;
                    if (inputSize > compressed.Length - sourceOffset || outputSize > dest.Length - targetOffset)
                        throw new InvalidDataException("Subblock poza buforem");

                    // XISF's subblock pair C,U with C==U denotes an uncompressed
                    // block, even if the Image advertises compression="zstd:...".
                    // The same rule holds for a single implicit block.
                    if (inputSize == outputSize)
                        Buffer.BlockCopy(compressed, sourceOffset, dest, targetOffset, outputSize);
                    else if (codec == "zlib")
                        DecompressZlib(compressed, sourceOffset, inputSize, dest, targetOffset, outputSize);
                    else if (codec == "lz4" || codec == "lz4hc")
                        DecompressLz4(compressed, sourceOffset, inputSize, dest, targetOffset, outputSize);
                    else
                    {
                        int written = zstd.Unwrap(compressed, sourceOffset, inputSize,
                            dest, targetOffset, outputSize);
                        if (written != outputSize)
                            throw new InvalidDataException("ZSTD: rozmiar danych po dekompresji nie zgadza się z XISF");
                    }
                    sourceOffset += inputSize;
                    targetOffset += outputSize;
                }
            }
            if (sourceOffset != compressed.Length || targetOffset != dest.Length)
                throw new InvalidDataException("Niekompletne dane skompresowane");
            if (shuffleSize > 1) return Unshuffle(dest, shuffleSize);
            return dest;
        }

        private static byte[] DecodeInline(string s, string encoding)
        {
            if (encoding.Equals("base64", StringComparison.OrdinalIgnoreCase)) return Convert.FromBase64String(s);
            if (encoding.Equals("base16", StringComparison.OrdinalIgnoreCase))
            {
                s = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
                if ((s.Length & 1) != 0) throw new InvalidDataException("Zła długość base16");
                var dest = new byte[s.Length / 2];
                for (int i = 0; i < dest.Length; i++) dest[i] = byte.Parse(s.Substring(i * 2, 2), NumberStyles.HexNumber);
                return dest;
            }
            throw new NotSupportedException("Nieobsługiwane kodowanie: " + encoding);
        }

        private static byte[] ReadExactly(Stream stream, int len)
        {
            var result = new byte[len];
            int pos = 0;
            while (pos < len)
            {
                int r = stream.Read(result, pos, len - pos);
                if (r == 0) throw new EndOfStreamException("Ucięty XISF");
                pos += r;
            }
            return result;
        }

        private static void DecompressZlib(byte[] src, int source, int bytes, byte[] dst, int offset, int expected)
        {
            if (bytes < 6 || (src[source] & 0x0f) != 8)
                throw new InvalidDataException("Nieprawidłowy nagłówek zlib");
            // .NET Framework 4.6.2 DeflateStream accepts raw deflate; skip zlib header and Adler32.
            using (var input = new MemoryStream(src, source + 2, bytes - 6, false))
            using (var inflator = new DeflateStream(input, CompressionMode.Decompress))
            {
                int written = 0;
                while (written < expected)
                {
                    int count = inflator.Read(dst, offset + written, expected - written);
                    if (count == 0) throw new InvalidDataException("Niepełny blok zlib");
                    written += count;
                }
                if (inflator.ReadByte() != -1) throw new InvalidDataException("Za długie dane zlib");
            }
        }

        private static void DecompressLz4(byte[] src, int start, int compressed, byte[] dst, int offset, int expected)
        {
            int ip = start, end = checked(start + compressed), op = offset, outEnd = checked(offset + expected);
            while (ip < end)
            {
                int token = src[ip++];
                int literals = token >> 4;
                if (literals == 15) literals = checked(literals + LengthExtension(src, ref ip, end));
                if (literals > end - ip || literals > outEnd - op) throw new InvalidDataException("LZ4: literały poza blokiem");
                Buffer.BlockCopy(src, ip, dst, op, literals);
                ip += literals; op += literals;
                if (ip == end) break;
                if (ip + 2 > end) throw new InvalidDataException("LZ4: brak offsetu");
                int displacement = src[ip++] | (src[ip++] << 8);
                if (displacement == 0 || displacement > op - offset) throw new InvalidDataException("LZ4: zły offset");
                int match = token & 15;
                if (match == 15) match = checked(match + LengthExtension(src, ref ip, end));
                match = checked(match + 4);
                if (match > outEnd - op) throw new InvalidDataException("LZ4: dopasowanie poza blokiem");
                for (int i = 0; i < match; i++) dst[op + i] = dst[op + i - displacement];
                op += match;
            }
            if (op != outEnd) throw new InvalidDataException("LZ4: niezgodny rozmiar danych");
        }

        private static int LengthExtension(byte[] src, ref int index, int end)
        {
            int total = 0, b;
            do
            {
                if (index >= end) throw new InvalidDataException("LZ4: ucięta długość");
                b = src[index++];
                total = checked(total + b);
            } while (b == 255);
            return total;
        }

        private static byte[] Unshuffle(byte[] data, int itemBytes)
        {
            int count = data.Length / itemBytes;
            var output = new byte[data.Length];
            for (int b = 0; b < itemBytes; b++)
                for (int i = 0; i < count; i++)
                    output[i * itemBytes + b] = data[b * count + i];
            int remainder = data.Length - count * itemBytes;
            if (remainder > 0) Buffer.BlockCopy(data, count * itemBytes, output, count * itemBytes, remainder);
            return output;
        }


        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] public uint Bits;
            [FieldOffset(0)] public float Number;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct DoubleBits
        {
            [FieldOffset(0)] public ulong Bits;
            [FieldOffset(0)] public double Number;
        }

        private sealed class PixelData
        {
            private readonly byte[] _bytes;
            private readonly int _channels, _bytesPerSample;
            private readonly string _format;
            private readonly bool _bigEndian, _normal;
            private readonly long _pixels;
            public int Width { get; }
            public int Height { get; }
            public int Channels => _channels;

            public PixelData(byte[] bytes, int width, int height, int channels, string format,
                int sampleSize, bool bigEndian, bool normal)
            {
                _bytes = bytes; Width = width; Height = height; _channels = channels;
                _format = format; _bytesPerSample = sampleSize;
                _bigEndian = bigEndian; _normal = normal;
                _pixels = (long)width * height;
            }

            public double Sample(int x, int y, int channel)
            {
                long pixel = (long)y * Width + x;
                long at = _normal ? pixel * _channels + channel : channel * _pixels + pixel;
                int offset = checked((int)(at * _bytesPerSample));
                return Decode(offset);
            }

            private double Decode(int offset)
            {
                uint u = _bytes[offset];
                if (_bytesPerSample >= 2) u = _bigEndian ? (u << 8) | _bytes[offset + 1]
                    : u | ((uint)_bytes[offset + 1] << 8);
                if (_bytesPerSample >= 4)
                {
                    if (_bigEndian) u = (u << 16) | ((uint)_bytes[offset + 2] << 8) | _bytes[offset + 3];
                    else u |= (uint)_bytes[offset + 2] << 16 | (uint)_bytes[offset + 3] << 24;
                }
                switch (_format)
                {
                    case "UInt8": return u / 255.0;
                    case "UInt16": return u / 65535.0;
                    case "Int16": return (short)u / 32767.0;
                    case "UInt32": return u / 4294967295.0;
                    case "Int32": return (int)u / 2147483647.0;
                    case "Float32":
                    {
                        var bits = new FloatBits { Bits = u };
                        return bits.Number;
                    }
                    case "Float64":
                    {
                        ulong l;
                        if (_bigEndian)
                            l = ((ulong)u << 32) | ((ulong)_bytes[offset + 4] << 24)
                                | ((ulong)_bytes[offset + 5] << 16) | ((ulong)_bytes[offset + 6] << 8)
                                | _bytes[offset + 7];
                        else
                            l = u | ((ulong)_bytes[offset + 4] << 32) | ((ulong)_bytes[offset + 5] << 40)
                                | ((ulong)_bytes[offset + 6] << 48) | ((ulong)_bytes[offset + 7] << 56);
                        var bits = new DoubleBits { Bits = l };
                        return bits.Number;
                    }
                    default: throw new NotSupportedException("sampleFormat: " + _format);
                }
            }
        }

        private sealed class Stretch
        {
            private readonly double _black, _range, _midtone;
            private Stretch(double black, double white, double midtone)
            {
                _black = black;
                _range = Math.Max(1e-12, white - black);
                _midtone = Math.Max(0.001, Math.Min(0.999, midtone));
            }

            public static Stretch Compute(PixelData image, CancellationToken cancel)
            {
                // Sample at most ~80k pixels, preserving channel balance (linked STF).
                long count = (long)image.Width * image.Height;
                int stride = Math.Max(1, (int)Math.Sqrt(count / 80000.0));
                var sampled = new List<double>(90000);
                for (int y = stride / 2; y < image.Height; y += stride)
                {
                    cancel.ThrowIfCancellationRequested();
                    for (int x = stride / 2; x < image.Width; x += stride)
                    {
                        // Use mean of RGB channels to estimate linked STF (better preserves color).
                        double v = image.Sample(x, y, 0);
                        if (image.Channels == 3)
                            v = (v + image.Sample(x, y, 1) + image.Sample(x, y, 2)) / 3.0;
                        if (!double.IsNaN(v) && !double.IsInfinity(v)) sampled.Add(v);
                    }
                }
                if (sampled.Count == 0) return new Stretch(0, 1, 0.5);
                sampled.Sort();
                double median = sampled[sampled.Count / 2];
                var deviations = sampled.Select(s => Math.Abs(s - median)).ToArray();
                Array.Sort(deviations);
                double mad = deviations[deviations.Length / 2] * 1.4826;
                double shadow = Math.Max(0, median - 2.8 * mad);
                double white = 1;
                // For very low contrast / zero-MAD data, use sample percentiles.
                if (mad <= 1e-12 || median - shadow < 1e-10)
                {
                    shadow = Math.Max(0, sampled[Math.Max(0, (int)(sampled.Count * .005))]);
                    white = Math.Max(shadow + 1e-6, sampled[Math.Min(sampled.Count - 1, (int)(sampled.Count * .998))]);
                    return new Stretch(shadow, white, 0.30);
                }
                double normalizedMedian = Math.Max(1e-9, Math.Min(.999, (median - shadow) / (white - shadow)));
                const double target = .25;
                // Inverse midtone transfer function: map median to target brightness.
                double midtone = normalizedMedian * (1 - target)
                    / (target + normalizedMedian * (1 - 2 * target));
                return new Stretch(shadow, white, midtone);
            }

            public byte Map(double sample)
            {
                if (double.IsNaN(sample) || double.IsInfinity(sample)) return 0;
                double v = (sample - _black) / _range;
                if (v <= 0) return 0;
                if (v >= 1) return 255;
                double denom = (2 * _midtone - 1) * v - _midtone;
                double output = Math.Abs(denom) < 1e-12 ? v : ((_midtone - 1) * v / denom);
                return (byte)Math.Max(0, Math.Min(255, (int)(output * 255 + .5)));
            }
        }
    }
}
