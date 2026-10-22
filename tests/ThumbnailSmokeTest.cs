// Compiled by Test.ps1 using the same .NET Framework runtime as the provider.
// Keep all IStream and out parameter calls in C#: PowerShell's binder cannot
// reliably bind COM RCWs to the managed IStream parameter of Initialize().
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using AstroXisf.ThumbnailProvider;

namespace AstroXisf.Test
{
    public static class ThumbnailSmokeTest
    {
        // STGM_READ = 0. This returns a real Windows COM IStream.
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHCreateStreamOnFileEx(
            [MarshalAs(UnmanagedType.LPWStr)] string file,
            uint mode, uint attributes,
            [MarshalAs(UnmanagedType.Bool)] bool create,
            IntPtr template,
            [MarshalAs(UnmanagedType.Interface)] out IStream result);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr handle);

        public static string Run(string inputPath, string outputPath)
        {
            if (String.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
                throw new FileNotFoundException("Nie znaleziono pliku XISF.", inputPath);
            if (String.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Nie podano sciezki pliku PNG.", "outputPath");

            IStream stream = null;
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                int hr = SHCreateStreamOnFileEx(inputPath, 0U, 0U, false, IntPtr.Zero, out stream);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                if (stream == null) throw new IOException("Windows nie zwrocil strumienia IStream.");

                XisfThumbnailProvider provider = new XisfThumbnailProvider();
                hr = provider.Initialize(stream, 0U);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);

                WtsAlpha alpha;
                hr = provider.GetThumbnail(256U, out bitmap, out alpha);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                if (bitmap == IntPtr.Zero) throw new IOException("Nie otrzymano uchwytu HBITMAP.");

                using (Bitmap preview = Bitmap.FromHbitmap(bitmap))
                {
                    preview.Save(outputPath, ImageFormat.Png);
                    return "OK: " + preview.Width + "x" + preview.Height + " px, alpha=" + alpha.ToString();
                }
            }
            finally
            {
                // Thumbnail provider gives ownership of the HBITMAP to the caller.
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (stream != null && Marshal.IsComObject(stream)) Marshal.ReleaseComObject(stream);
            }
        }
    }
}
