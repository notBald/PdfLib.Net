using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Write.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Function;
using PdfLib.Pdf.Filter;
using PdfLib.Util;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    public abstract class PdfStreamShading : PdfShading, ICStream
    {
        #region Variables and properties

        internal sealed override SM DefSaveMode { get { return SM.Indirect; } }

        protected readonly IStream _stream;

        /// <summary>
        /// The number of bits used to represent each geometric coordinate.
        /// </summary>
        /// <remarks>The value shall be 1, 2, 4, 8, 12, 16, 24, or 32</remarks>
        public int BitsPerCoordinate { get { return _elems.GetUIntEx("BitsPerCoordinate"); } }

        /// <summary>
        /// The number of bits used to represent each colour component.
        /// </summary>
        /// <remarks>The value shall be 1, 2, 4, 8, 12, or 16</remarks>
        public int BitsPerComponent { get { return _elems.GetUIntEx("BitsPerComponent"); } }

        /// <summary>
        /// The number of bits used to represent the edge flag for each patch
        /// </summary>
        /// <remarks>
        /// The value shall be 2, 4, or 8, but only the least significant 2 
        /// bits in each flag value shall be used.
        /// </remarks>
        public int BitsPerFlag { get { return _elems.GetUIntEx("BitsPerFlag"); } }

        /// <summary>
        /// An array of numbers specifying how to map coordinates and colour
        /// components into the appropriate ranges of values.
        /// </summary>
        /// <remarks>xmin, xmax, ymin, ymax, coln min, coln max</remarks>
        public xRange[] Decode
        {
            get { return xRange.Create((RealArray)_elems.GetPdfTypeEx("Decode", PdfType.RealArray)); }
        }

        /// <summary>
        /// A 1-in, n-out function or an array of n 1-in, 1-out functions
        /// </summary>
        public PdfFunctionArray Function
        {
            get
            {
                return (PdfFunctionArray)_elems.GetPdfType("Function", PdfType.FunctionArray,
                    PdfType.Array, false, IntMsg.NoMessage, null);
            }
        }

        #endregion

        #region Init

        internal PdfStreamShading(IWStream stream, IColorSpace color_space, PdfFunctionArray functions, int bp_coord, int bpc, int shading_type)
            : base(color_space, shading_type, stream.Elements)
        {
            _stream = stream;

            var ws = (WritableStream)_stream;
            var d = ws.Elements;

            if (functions != null)
                d.SetItem("Function", functions, true);

            if (bp_coord != 1 && bp_coord != 2 && bp_coord != 4 && bp_coord != 8 &&
                bp_coord != 12 && bp_coord != 16 && bp_coord != 24 && bp_coord != 32)
                throw new PdfNotSupportedException("Shader bitsize of " + bp_coord);

            if (bpc != 1 && bpc != 2 && bpc != 4 && bpc != 8 &&
                bpc != 12 && bpc != 16)
                throw new PdfNotSupportedException("Shader color bitsize of " + bpc);

            d.SetInt("BitsPerCoordinate", bp_coord);
            d.SetInt("BitsPerComponent", bpc);
            d.SetInt("BitsPerFlag", 2);
        }

        internal PdfStreamShading(IWStream stream)
            : base(stream.Elements)
        { _stream = stream; }

        protected PdfStreamShading(PdfDictionary dict, IStream stream)
            : base(dict)
        {
            _stream = stream;
        }

        #endregion

        #region ICStream

        void ICStream.Compress(List<FilterArray> filters, CompressionMode mode)
        {
            ((ICStream)_stream).Compress(filters, mode);
        }

        PdfDictionary ICStream.Elements { get { return _elems; } }

        void ICStream.LoadResources()
        {
            ((IWStream)_stream).LoadResources();
        }

        #endregion

        protected override bool Equivalent(PdfShading obj)
        {
            var shade = (PdfStreamShading)obj;
            return
                BitsPerCoordinate == shade.BitsPerCoordinate &&
                BitsPerComponent == shade.BitsPerComponent &&
                BitsPerFlag == shade.BitsPerFlag &&
                xRange.Compare(Decode, shade.Decode) &&
                Function.Equivalent(shade.Function) &&
                ArrayHelper.ArraysEqual(_stream.RawStream, shade._stream.RawStream);
        }

        internal sealed override void Write(PdfWriter write)
        {
            throw new PdfInternalException("Patch shadings can't be saved as a direct object");
        }

        internal override void Write(PdfWriter write, SM store_mode)
        {
            if (store_mode == SM.Direct)
                throw new PdfInternalException("Patch shadings can't be saved as a direct object");
            ((IWStream)_stream).Write(write);
        }

        protected sealed override Elements MakeCopy(PdfDictionary elems)
        {
            return MakeCopy(((IWStream)_stream).MakeCopy((WritableDictionary)elems), elems);
        }

        protected override void DictChanged()
        {
            ((IWStream)_stream).SetDictionary(_elems);
        }

        protected override void LoadResources()
        {
            ((IWStream)_stream).LoadResources();
        }

        protected abstract PdfStreamShading MakeCopy(IStream stream, PdfDictionary dict);
    }
}
