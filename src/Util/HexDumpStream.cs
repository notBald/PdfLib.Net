using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PdfLib.Util
{
    /// <summary>
    /// Debug utility class that provides a hex dump of stream data written to this stream
    /// </summary>
    public class HexDumpStream : Stream
    {
        MemoryStream ms;

        public HexDumpStream(MemoryStream ms)
        { this.ms = ms; }
        public HexDumpStream()
            : this(new MemoryStream())
        { }
        public byte[] ToArray() { return ms.ToArray(); }
        public override bool CanRead { get { return ms.CanRead; } }
        public override bool CanSeek { get { return ms.CanSeek; } }
        public override bool CanTimeout { get { return ms.CanTimeout; } }
        public override bool CanWrite { get { return ms.CanWrite; } }
        public override void Close() { ms.Close(); }
        public override long Length { get { return ms.Length; } }
        public override long Position
        {
            get { return ms.Position; }
            set { ms.Position = value; }
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            return ms.Read(buffer, offset, count);
        }
        public override int ReadByte()
        {
            return ms.ReadByte();
        }
        const int stop_at = 0x10FB0;
        public override void Write(byte[] buffer, int offset, int count)
        {
            //if (ms.Position + offset + count >= stop_at)
            //    System.Diagnostics.Debug.Assert(false);
            ms.Write(buffer, offset, count);
        }
        public override void WriteByte(byte value)
        {
            //if (ms.Position == stop_at)
            //    System.Diagnostics.Debug.Assert(false);
            ms.WriteByte(value);
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            return ms.Seek(offset, origin);
        }
        public override void SetLength(long value)
        {
            ms.SetLength(value);
        }
        public override void Flush()
        {
            ms.Flush();
        }

        public string HexDump
        {
            get
            {
                return PdfLib.Pdf.Filter.PdfHexFilter.HexDump(ms.ToArray(), new int[] { 8, 8 });
            }
        }

        public string PosHexDump
        {
            get
            {
                if (ms.Position + 1 >= ms.Length)
                    return "Position = Length";
                var ba = new byte[(int)ms.Position];
                var hold = ms.Position;
                ms.Position = 0;
                ms.Read(ba, 0, ba.Length);
                ms.Position = hold;
                return PdfLib.Pdf.Filter.PdfHexFilter.HexDump(ba, new int[] { 8, 8 });
            }
        }
    }
}
