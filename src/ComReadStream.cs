using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace AstroXisf.ThumbnailProvider
{
    /// <summary>
    /// Seekable, read-only wrapper for the IStream supplied by Explorer.
    /// Reads directly from the shell stream; never creates huge temporary XISF copies.
    /// </summary>
    internal sealed class ComReadStream : Stream
    {
        private readonly IStream stream;
        internal ComReadStream(IStream input)
        {
            stream = input ?? throw new ArgumentNullException(nameof(input));
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                stream.Stat(out System.Runtime.InteropServices.ComTypes.STATSTG stat, 1);
                return stat.cbSize;
            }
        }

        public override long Position
        {
            get => Seek(0, SeekOrigin.Current);
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException();
            if (count == 0) return 0;

            // IStream.Read cannot write into a managed array at an arbitrary offset.
            // Use a bounded scratch buffer to avoid allocating a second giant pixel array.
            int requested = Math.Min(count, 64 * 1024);
            byte[] chunk = offset == 0 && count <= requested ? buffer : new byte[requested];
            IntPtr readPtr = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(readPtr, 0);
                stream.Read(chunk, requested, readPtr);
                int read = Marshal.ReadInt32(readPtr);
                if (read < 0 || read > requested) throw new IOException("Nieprawidlowa liczba bajtow z IStream.Read");
                if (!ReferenceEquals(chunk, buffer) && read > 0)
                    Buffer.BlockCopy(chunk, 0, buffer, offset, read);
                return read;
            }
            finally { Marshal.FreeHGlobal(readPtr); }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(long));
            try
            {
                stream.Seek(offset, (int)origin, ptr);
                return Marshal.ReadInt64(ptr);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
