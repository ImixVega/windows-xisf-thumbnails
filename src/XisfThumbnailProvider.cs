using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using QuickLook.Plugin.XisfViewer; // reuse the tested decoder from QuickLook v0.2

[assembly: ComVisible(false)]

namespace AstroXisf.ThumbnailProvider
{
    // Windows Shell interface IDs are specified by Microsoft, not invented GUIDs.
    [ComVisible(true)]
    [Guid("B824B49D-22AC-4161-AC8A-9916E8FA3F7F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithStream
    {
        [PreserveSig]
        int Initialize([MarshalAs(UnmanagedType.Interface)] IStream stream, uint grfMode);
    }

    [ComVisible(true)]
    [Guid("E357FCCD-A995-4576-B01F-234630154E96")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailProvider
    {
        [PreserveSig]
        int GetThumbnail(uint cx, out IntPtr phbmp, out WtsAlpha pdwAlpha);
    }

    public enum WtsAlpha : uint
    {
        Unknown = 0,
        Rgb = 1,
        Argb = 2
    }

    /// <summary>
    /// COM thumbnail handler registered only for .xisf. Standard shell surrogate
    /// isolation stays ON; we don't set DisableProcessIsolation in the registry.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    [Guid("68F756A5-4977-4DF0-B306-AE9812B57EC8")]
    [ProgId("AstroXisf.ThumbnailProvider")]
    public sealed class XisfThumbnailProvider : IInitializeWithStream, IThumbnailProvider
    {
        private IStream input;
        private bool initialized;
        private const int EFail = unchecked((int)0x80004005);
        private const int EInvalidArg = unchecked((int)0x80070057);
        private const int EUnexpected = unchecked((int)0x8000FFFF);

        public int Initialize(IStream stream, uint grfMode)
        {
            if (stream == null) return EInvalidArg;
            if (initialized) return EUnexpected;
            input = stream;
            initialized = true;
            return 0;
        }

        public int GetThumbnail(uint cx, out IntPtr phbmp, out WtsAlpha pdwAlpha)
        {
            phbmp = IntPtr.Zero;
            pdwAlpha = WtsAlpha.Unknown;
            if (!initialized || input == null || cx < 1) return EInvalidArg;

            try
            {
                var maxEdge = (int)Math.Min(1024U, Math.Max(16U, cx));
                PreviewImage picture;
                using (var stream = new ComReadStream(input))
                    picture = XisfDecoder.Preview(stream, CancellationToken.None, maxEdge);

                // Shell requests <= cx. Small sizes are handled by final scaling.
                int width = picture.Width, height = picture.Height;
                if (width > cx || height > cx)
                {
                    double f = Math.Min(cx / (double)width, cx / (double)height);
                    int newWidth = Math.Max(1, (int)Math.Round(width * f));
                    int newHeight = Math.Max(1, (int)Math.Round(height * f));
                    var resampled = new byte[newWidth * newHeight * 3];
                    for (int y = 0; y < newHeight; y++)
                    {
                        int sy = Math.Min(height - 1, (int)((y + .5) * height / newHeight));
                        for (int x = 0; x < newWidth; x++)
                        {
                            int sx = Math.Min(width - 1, (int)((x + .5) * width / newWidth));
                            Buffer.BlockCopy(picture.Bgr, (sy * width + sx) * 3,
                                resampled, (y * newWidth + x) * 3, 3);
                        }
                    }
                    picture.Bgr = resampled;
                    picture.Width = newWidth;
                    picture.Height = newHeight;
                }
                phbmp = MakeDibSection(picture);
                pdwAlpha = WtsAlpha.Argb;
                return 0;
            }
            catch (Exception ex)
            {
                LogFailure(ex);
                return EFail;
            }
        }

        // Microsoft requires a 32-bit-per-pixel DIB section rather than GetHbitmap DDB.
        private static IntPtr MakeDibSection(PreviewImage image)
        {
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)),
                    Width = image.Width,
                    Height = -image.Height, // top-down DIB: no vertical flip
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = checked((uint)(image.Width * image.Height * 4))
                }
            };
            IntPtr bits;
            IntPtr hBitmap = CreateDIBSection(IntPtr.Zero, ref info, 0, out bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
                throw new IOException("Windows nie moze utworzyc 32-bitowego DIB");
            try
            {
                var bgra = new byte[checked(image.Width * image.Height * 4)];
                for (int i = 0, j = 0; i < image.Bgr.Length; i += 3, j += 4)
                {
                    bgra[j] = image.Bgr[i];
                    bgra[j + 1] = image.Bgr[i + 1];
                    bgra[j + 2] = image.Bgr[i + 2];
                    bgra[j + 3] = 255;
                }
                Marshal.Copy(bgra, 0, bits, bgra.Length);
                return hBitmap; // ownership goes to Windows on success
            }
            catch
            {
                DeleteObject(hBitmap);
                throw;
            }
        }

        private static void LogFailure(Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AstroXisfThumbnail");
                Directory.CreateDirectory(dir);
                string log = Path.Combine(dir, "errors.log");
                if (File.Exists(log) && new FileInfo(log).Length > 1024 * 1024)
                    File.Delete(log);
                File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + ex + Environment.NewLine);
            }
            catch { /* Thumbnail errors must not take down the shell. */ }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader Header;
            public uint UnusedColor; // BITMAPINFO has a flexible-length rgbColors array.
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(
            IntPtr hdc, ref BitmapInfo bmi, uint usage,
            out IntPtr bits, IntPtr hSection, uint offset);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
