using System;
using System.IO;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Filter
{
    public sealed class PdfFaxFilter : PdfFilter
    {
        /// <summary>
        /// This filter's PDF name
        /// </summary>
        public override string Name { get { return "CCITTFaxDecode"; } }

        public override byte[] Decode(byte[] data, PdfFilterParms fparams)
        {
            bool eod;
            return Decode(data, fparams, out eod);
        }

        /// <summary>
        /// Decodes a CCITT image
        /// </summary>
        /// <param name="data">Data to decode</param>
        /// <param name="fparams">Params for decoding</param>
        /// <param name="EOD">True if decoding ended by EOD</param>
        /// <returns>Decoded data</returns>
        /// <remarks>Only for use when decoding inline images</remarks>
        internal byte[] Decode(byte[] data, PdfFilterParms fparams, out bool EOD)
        {
            var fax = (PdfFaxParms)fparams;

            int k_encoding = fax.K;
            int width = fax.Columns;
            int height = fax.Rows;
            if (k_encoding < 0)
            {
                //There some experimental, never tested, EOL stuff in the G4 decoder.
                if (fax.EndOfLine) throw new NotImplementedException("G4 with EOL.");
                data = Img.CCITTDecoder.DecodeG4(width, height, data, fax.EndOfBlock, fax.BlackIs1, fax.EndOfLine, fax.EncodedByteAlign, out EOD);
            }
            else
                data = Img.CCITTDecoder.DecodeG3(width, height, data, false, fax.EncodedByteAlign, fax.BlackIs1, fax.EndOfBlock, fax.EndOfLine, (k_encoding == 0) ? 0 : 1, out EOD);
            return data;
        }

#if LONGPDF
        internal override byte[] FindEnd(Stream source, long startpos, PdfFilterParms fparams)
#else
        internal override byte[] FindEnd(Stream source, int startpos, PdfFilterParms fparams)
#endif
        {
#if true
            //By returning null we tell "don't know how" and a EI search will be performed instead.
            return null;
#else
            //This code does not work on atec-2008-01.pdf page 4, which indicates that there's something
            //not right about how I find the end of the CCITT data. 
            // (Remeber to enable streams support in CCITT decoder)
            var fax = (PdfFaxParms)fparams;

            int k_encoding = fax.K;
            int width = fax.Columns;
            int height = fax.Rows;
            var rec = new Util.RecordStream(4096, source);
            bool EOD;
            if (k_encoding < 0)
            {
                //There some experimental, never tested, EOL stuff in the G4 decoder.
                if (fax.EndOfLine) 
                {
                    System.Diagnostics.Debug.Assert(false, "not implemented");
                    return null;
                }
                source.Position = startpos;
                Util.CCITTDecoder.DecodeG4(width, height, rec, fax.EndOfBlock, fax.BlackIs1, fax.EndOfLine, fax.EncodedByteAlign, out EOD);
            }
            else if (k_encoding == 0)
            {
                source.Position = startpos;
                Util.CCITTDecoder.DecodeG3(width, height, rec, false, fax.EncodedByteAlign, fax.EndOfBlock, fax.EndOfLine, 0, out EOD);
            }
            else 
            {
                    System.Diagnostics.Debug.Assert(false, "not implemented");
                    return null;
            }

            return rec.ToArray();
#endif
        }

        public override bool Equals(PdfFilter filter)
        {
            return filter is PdfFaxFilter;
        }

        public override string ToString()
        {
            return "/CCITTFaxDecode";
        }
    }

    public class PdfFaxParms : PdfFilterParms
    {
        public int K { get { return _elems.GetInt("K", 0); } }

        /// <summary>
        /// If one should byte aling after a row
        /// </summary>
        public bool EncodedByteAlign { get { return _elems.GetBool("EncodedByteAlign", false); } }

        /// <summary>
        /// The height of the image in scan lines. If the value is 0 or 
        /// absent, the image’s height is not predetermined, and the 
        /// encoded data shall be terminated by an end-of-block bit 
        /// pattern or by the end of the filter’s data.
        /// </summary>
        public int Rows { get { return _elems.GetUInt("Rows", 0); } }

        /// <summary>
        /// A flag indicating whether the filter shall expect the encoded 
        /// data to be terminated by an end-of-block pattern
        /// </summary>
        public bool EndOfBlock { get { return _elems.GetBool("EndOfBlock", true); } }

        /// <summary>
        /// Whenever one can expect EoL
        /// </summary>
        public bool EndOfLine { get { return _elems.GetBool("EndOfLine", false); } }

        /// <summary>
        /// Flips the white/black colors
        /// </summary>
        public bool BlackIs1 { get { return _elems.GetBool("BlackIs1", false); } }

        public int DamagedRowsBeforeError { get { return _elems.GetUInt("DamagedRowsBeforeError", 0); } }

        /// <summary>
        /// The width of the image in pixels. 
        /// If the value is not a multiple of 8, the filter shall adjust the width 
        /// of the unencoded image to the next multiple of 8
        /// </summary>
        public int Columns { get { return _elems.GetUInt("Columns", 1728); } }

        /// <summary>
        /// Creates a new fax param dictionary
        /// </summary>
        /// <param name="k">Negative for G4, 0 for G3 and postitive for G32D</param>
        /// <param name="columns">Width</param>
        /// <param name="rows">Height</param>
        /// <param name="byte_align">Encoded data is byte aligned on rows</param>
        /// <param name="end_of_block">There are end of block markers</param>
        /// <param name="end_of_line">There are end of line markers</param>
        /// <param name="black_is_1">Black is to be considered 1</param>
        /// <param name="damaged_rows">Number of consecutive damaged rows in the data</param>
        public PdfFaxParms(int? k, int? columns, int? rows, bool byte_align, bool end_of_block, bool end_of_line, bool black_is_1,
            int? damaged_rows)
            : base(new PdfLib.Write.Internal.TemporaryDictionary())
        {
            if (k != null && k.Value != 0)
                _elems.SetInt("K", k.Value);
            if (byte_align)
                _elems.SetBool("EncodedByteAlign", true);
            if (rows != null && rows.Value != 0)
                _elems.SetInt("Rows", rows.Value);
            if (columns != null && columns.Value != 1728)
                _elems.SetInt("Columns", columns.Value);
            if (!end_of_block)
                _elems.SetBool("EndOfBlock", false);
            if (end_of_line)
                _elems.SetBool("EndOfLine", true);
            if (black_is_1)
                _elems.SetBool("BlackIs1", true);
            if (damaged_rows != null && damaged_rows.Value != 0)
                _elems.SetInt("DamagedRowsBeforeError", damaged_rows.Value);
        }

        internal PdfFaxParms(PdfDictionary dict)
            : base(dict)
        { }

        /// <summary>
        /// For moving the element to a different document.
        /// </summary>
        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfFaxParms(elems);
        }
    }
}
