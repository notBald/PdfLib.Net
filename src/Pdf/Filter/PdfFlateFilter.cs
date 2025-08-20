//For using ZLib instead of DeflateStream for decompression
//Note: There's nothing wrong with the built in deflate implementation, the problem comes when
//      dealing with inline images. ZLib is used for inline images regadless of this setting.
#define ZLibNet //Only reason to keep this on is to test the implementation better. 

//Uses .net zip implementation for inline images
//#define UseNetZip

//For using zlib for compression instead of DeflateStream
//Note: With improvments in the built in deflate implementation, I'm seeing it perform faster and 
//      better than ZLibNet with standar parameters. (But worse than zlib at highest compression)
#define encodeWithZLibNet
//#define ZopfliEncode //Needs encodeWidthZLib
using System.IO.Compression;
using System.IO;
using System;

namespace PdfLib.Pdf.Filter
{
    /// <summary>
    /// Implements zip decompression through use of the built in .net libary, or
    /// alternativly zlib
    /// 
    /// PNG and Tiff predictors are also supported
    /// </summary>
    /// <remarks>
    /// http://connect.microsoft.com/VisualStudio/feedback/ViewFeedback.aspx?FeedbackID=97064
    /// Used workaround by Sean Hederman
    /// 
    /// .Net's impl of deflate is poor:
    /// http://www.virtualdub.org/blog/pivot/entry.php?id=335#body
    /// 
    /// To get everything into one dll investigate:
    /// http://www.microsoft.com/download/en/details.aspx?displaylang=en&id=17630
    /// </remarks>
    public sealed class PdfFlateFilter : PdfFilter
    {
        /// <summary>
        /// This filter's PDF name
        /// </summary>
        public override string Name { get { return "FlateDecode"; } }

        /// <summary>
        /// Value (in bytes) for when to not bother with zip compression. 
        /// </summary>
        public const int SKIP = 16;

        public override byte[] Decode(byte[] data, PdfFilterParms fparams)
        {
#if ZLibNet
            var inflate = new DeflateLib.zInflate();
            inflate.avail_in = data.Length;
            inflate.SetNextIn(data, 0);
            DeflateLib.zRetCode err = DeflateLib.zRetCode.Z_ERRNO;
            int buff_size = Math.Max(512, Math.Min(1024 * 1024, data.Length * 4));
            byte[][] buffers = null;
            int buffer_count = 0;
            inflate.OutputBuffer = new byte[buff_size];
            do
            {
                if (err == DeflateLib.zRetCode.Z_OK)
                {
                    if (buffers == null)
                        buffers = new byte[16][];
                    else if (buffer_count == buffers.Length)
                        Array.Resize<byte[]>(ref buffers, buffers.Length * 2);

                    buffers[buffer_count++] = inflate.OutputBuffer;
                    inflate.OutputBuffer = new byte[buff_size];
                }
                inflate.OutputPosition = 0;
                inflate.avail_out = buff_size;
                err = inflate.Inflate(DeflateLib.zFlush.Z_NO_FLUSH);

                if (err != DeflateLib.zRetCode.Z_OK)
                {
                    if (err == DeflateLib.zRetCode.Z_STREAM_END)
                    {
                        err = DeflateLib.zRetCode.Z_OK;
                        break;
                    }
                    throw new EndOfStreamException("Deflate error: " + inflate.msg);
                }

                //Issue: How do you reliably tell that inflate is done?
                //The problem is that even when avail_in is 0, there may
                //still be data left to fetch. 
            } while (inflate.avail_in != 0 || inflate.avail_out == 0 && inflate.HasDataLeftToOutput);

            //err = inflate.Inflate(DeflateLib.zFlush.Z_FINISH);
            if (err != DeflateLib.zRetCode.Z_OK)
                throw new EndOfStreamException("Deflate error: " + inflate.msg);

            var ret = new byte[(int)inflate.total_out];
            int offset = 0;
            if (buffers != null)
            {
                for (int c = 0; c < buffer_count; c++)
                {
                    Buffer.BlockCopy(buffers[c], 0, ret, offset, buff_size);
                    offset += buff_size;
                }
            }
            Buffer.BlockCopy(inflate.OutputBuffer, 0, ret, offset, (int)(inflate.total_out - offset));
            if (fparams == null)
                return ret;

            using (var dflate = new MemoryStream(ret))
            {
#else 
            using (var ms = new MemoryStream(data))
            {
                ms.Position = 2; //<- workaround by Sean Hederman
                using (var dflate = new DeflateStream(ms, CompressionMode.Decompress))
#endif
                {
                    if (fparams != null)
                    {
                        var fp = (FlateParams)fparams.Deref();
                        var pred = fp.Predictor;
                        if (pred > Predictor.None)
                        {
                            if (pred == Predictor.Tiff2)
                                //This predictor is pretty simple, basically PRED.Sub just
                                //that it works on pixel component values instead of bytes.
                                // So a 4BBC gray image must be handled differently than a 
                                // 8BPC RGB image.
                                return PNGPredictor.TiffPredicor(dflate, fp.Colors, fp.BitsPerComponent, fp.Columns);

                            //According to 7.4.4.4 all PNG predicors are to be treated
                            //the same way.
                            if (pred <= Predictor.PNG_Opt)
                                return PNGPredictor.Recon(dflate, fp.Colors, fp.BitsPerComponent, fp.Columns);

                            System.Diagnostics.Debug.Assert(false);
                            throw new NotImplementedException("Predicor: " + pred);
                        }
                    }

                    //Reads out the compressed data. This is perhaps a tad inefficent though.
                    using (var ms2 = new MemoryStream(Math.Min(data.Length * 10, 1024*1024)))
                    {
                        var read = new byte[2048];
                        int numRead;
                        while ((numRead = dflate.Read(read, 0, read.Length)) > 0)
                            ms2.Write(read, 0, numRead);

                        return ms2.ToArray();
                    }
                }
            }
        }

        /// <summary>
        /// Primarly intended for support of inline images. Finds the end of the source
        /// data and returns it.
        /// </summary>
        /// <param name="source">source stream</param>
        /// <param name="startpos">From where to start reading in the stream</param>
        /// <param name="fparams">Ignored, as it has no impact on the source data</param>
        /// <returns>Source data from start position to the end</returns>
#if LONGPDF
        internal override byte[] FindEnd(Stream source, long startpos, PdfFilterParms fparams)
#else
        internal override byte[] FindEnd(Stream source, int startpos, PdfFilterParms fparams)
#endif
        {
            source.Position = startpos;
#if !UseNetZip
            var inflate = new DeflateLib.zInflate();

            //Start with a small buffer. Inline images are usualy small
            const int buff_size = 64;

            //We're not interested in the output. We have no idea about the input size,
            //so starting with a 64 byte output buffer is just as well
            inflate.SetNextOut(new byte[buff_size*2], 0);

            DeflateLib.zRetCode err = DeflateLib.zRetCode.Z_ERRNO;

            //Contains all the read buffers we make.
            byte[][] buffers = null;
            int buffer_count = 0;

            //Sets the first read buffer
            var read_buff = inflate.InputtBuffer = new byte[buff_size];

            while(true)
            {
                //We always start reading from 0, and since we don't care about
                //the output, it does not hurt to zero that as well.
                inflate.OutputPosition = 0;
                inflate.avail_out = inflate.OutputBuffer.Length;

                //If inflate.avali_in != 0, it means it ran out of output buffer space.
                if (inflate.avail_in == 0)
                {
                    inflate.InputtPosition = 0;

                    //A read command need not fill the entire buffer, so we set
                    //avalible to how many bytes it read
                    inflate.avail_in = source.Read(read_buff, 0, read_buff.Length);
                    if (inflate.avail_in == 0)
                        break; //Stream has ended
                }

                //Decompresses the data
                err = inflate.Inflate(DeflateLib.zFlush.Z_NO_FLUSH);

                //Checks if compression has ended, breaks if so
                if (err == DeflateLib.zRetCode.Z_STREAM_END)
                    break;

                //Some errors are recoverable, but they should never be returned in
                //this impel.
                if (err != DeflateLib.zRetCode.Z_OK)
                    throw new EndOfStreamException("Deflate error: " + inflate.msg);

                //If inflate.avali_in != 0, it means it ran out of output buffer space.
                //Might be an idea to enlarge the output buffer.
                if (inflate.avail_in == 0)
                {
                    if (buffers == null)
                        buffers = new byte[16][];
                    else if (buffer_count == buffers.Length)
                        Array.Resize<byte[]>(ref buffers, buffers.Length * 2);

                    buffers[buffer_count++] = read_buff;
                    inflate.InputtBuffer = read_buff = new byte[buff_size + buff_size * buffer_count];
                    inflate.OutputBuffer = new byte[read_buff.Length * 2];
                }
            }

            //Creates an array large enough for the image
            var ret = new byte[(int)inflate.total_in];

            //Copies from the read buffers
            int offset = 0;
            if (buffers != null && inflate.total_in > buff_size)
            {
                for (int c = 0; c < buffer_count; c++)
                {
                    var buff = buffers[c];
                    Buffer.BlockCopy(buff, 0, ret, offset, buff.Length);
                    offset += buff.Length;
                }
            }

            //Makes the final copy.
            Buffer.BlockCopy(read_buff, 0, ret, offset, (int)(inflate.total_in - offset));

            return ret;            
#else
            var rec = new Util.RecordStream(4096, source);
            rec.Position += 2; //<- workaround by Sean Hederman
            var df = new DeflateStream(rec, CompressionMode.Decompress);

            //Reads the whole stream
            while (df.ReadByte() != -1) ;

            //Problem: Can't get the amount of data actually read by the .net lib. The record
            //         stream will know how much data has been retrieved, but not how much
            //         of that data was actually used. I.e. the returned array will contain 
            //         junk data.
            return rec.ToArray();
#endif
        }

        public override bool Equals(PdfFilter filter) { return filter is PdfFlateFilter; }

        public override string ToString() { return "/FlateDecode"; }

        public static byte[] Encode(byte[] data)
        {
#if encodeWithZLibNet
#if ZopfliEncode
            var output = new MemoryStream(data.Length);
            output.WriteByte(0x78);
            output.WriteByte(0x9c); //<-- Blind guess
            var zle = new DeflateLib.Zopfli.ZopfliDeflater(output);
            if (data.Length > 50000)
                zle.NumberOfIterations = 5;
            zle.Deflate(data, true);
            return output.ToArray();
#else
            //-1 gives the default compression level, the max level is 9. Level 9
            //does give better compression but in my testing it's not worth the
            //extra compression time.
            var z = new DeflateLib.zDeflate(DeflateLib.zCOMPRESSION.DEFAULT);
            z.InputtBuffer = data;
            z.avail_in = data.Length;
            int buff_size = Math.Max(512, Math.Min(1024*1024, data.Length));
            z.OutputBuffer = new byte[buff_size];
            DeflateLib.zRetCode err = DeflateLib.zRetCode.Z_ERRNO;
            byte[][] buffers = null;
            int buffer_count = 0;

            do
            {
                if (err == DeflateLib.zRetCode.Z_OK)
                {
                    if (buffers == null)
                        buffers = new byte[16][];
                    else if (buffer_count == buffers.Length)
                        Array.Resize<byte[]>(ref buffers, buffers.Length * 2);

                    buffers[buffer_count++] = z.OutputBuffer;
                    z.OutputBuffer = new byte[buff_size];
                }

                z.NextOut = null;
                z.avail_out = buff_size;
                err = z.Deflate(DeflateLib.zFlush.Z_NO_FLUSH);

                if (err != DeflateLib.zRetCode.Z_OK)
                {
                    //File.WriteAllBytes("FailedToCompress.zlib", data);
                    throw new EndOfStreamException("Deflate error: " + z.msg);
                }

            } while (z.avail_in != 0);

        retry:
            if (z.avail_out != 0) //Will simply return a buffer error if there's no space avalible, so we fall through to err == DeflateLib.zRetCode.Z_OK
            {
                

                err = z.Deflate(DeflateLib.zFlush.Z_FINISH);
            }
            if (err == DeflateLib.zRetCode.Z_OK)
            {
                //This can happen when a file is larger after deflation or when z.avail_out == 0.

                if (buffers == null)
                    buffers = new byte[1][];
                else if (buffer_count == buffers.Length)
                    Array.Resize<byte[]>(ref buffers, buffers.Length * 2);

                buffers[buffer_count++] = z.OutputBuffer;
                z.OutputBuffer = new byte[buff_size];

                z.NextOut = null;
                z.avail_out = buff_size;
                goto retry;
            }
            if (err != DeflateLib.zRetCode.Z_STREAM_END)
            {
                //File.WriteAllBytes("FailedToCompress.zlib", data);
                throw new EndOfStreamException("Deflate error: " + z.msg);
            }
            z.DeflateEnd();

            var ret = new byte[(int)z.total_out];
            int offset = 0;
            if (buffers != null)
            {
                for (int c = 0; c < buffer_count; c++)
                {
                    Buffer.BlockCopy(buffers[c], 0, ret, offset, buff_size);
                    offset += buff_size;
                }
            }
            Buffer.BlockCopy(z.OutputBuffer, 0, ret, offset, (int) (z.total_out - offset));
            return ret;
#endif
#else
            using (var dest = new MemoryStream(data.Length))
            {                
                using (var zipper = new DeflateStream(dest, CompressionMode.Compress))
                {
                    dest.WriteByte(0x78);
                    dest.WriteByte(0x9c); //<-- Blind guess
                
                    zipper.Write(data, 0, data.Length);
                }

                return dest.ToArray();
            }
#endif
        }

        /// <summary>
        /// Convinience method. Not in use by the solution.
        /// </summary>
        public static MemoryStream DecodeGzip(Stream read)
        {//Note: ZLib also supports gzip, but only if compiled with "GUNZIP"
            var ms = new MemoryStream((int) read.Length * 2);
            using (var gzip = new GZipStream(read, CompressionMode.Decompress))
            {
                var l = (int) read.Length;
                int bytes = 0;
                var ba = new byte[4096];
                while (bytes < l)
                {
                    int r = gzip.Read(ba, 0, ba.Length);
                    ms.Write(ba, 0, r);
                    bytes += r;
                }
            }
            ms.Position = 0;
            return ms;
        }
    }
}