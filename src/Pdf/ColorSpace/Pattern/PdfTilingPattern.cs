using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Filter;
using PdfLib.Write.Internal;
using PdfLib.Render;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    /// <remarks>
    /// I'm thinking about using the tile functionality of WPF instead of
    /// redrawing the tile each time, but first how this behaves with pattern
    /// fills needs to be investiated.
    /// 
    /// I.e. 1. Make a tiling pattern that fills a shape with a pattern
    ///      2. Make a uncolored tiling pattern that fills a shape with the current color.
    ///      
    /// For now the implementation is slow but correct, so no rush.
    /// </summary>
    public sealed class PdfTilingPattern : PdfPattern, IWSPage, Compile.IForm, ICStream
    {
        #region Variables and properties

        private IWStream _content;

        /// <summary>
        /// Stream objects must always be indirect
        /// </summary>
        internal override SM DefSaveMode { get { return SM.Indirect; } }

        /// <summary>
        /// The content as text
        /// </summary>
        /// <remarks>Using ASCII, but a correct string much be raw</remarks>
        public string ContentText { get { return Encoding.ASCII.GetString(_content.DecodedStream); } }

        public PdfRectangle BBox
        {
            get
            {
                return (PdfRectangle)_elems.GetPdfTypeEx("BBox", PdfType.Rectangle);
            }
            set
            {
                if (value == null) throw new ArgumentNullException();
                _elems.SetItem("BBox", value, false);
            }
        }

        public PdfResources Resources
        {
            get
            {
                return (PdfResources) _elems.GetPdfTypeEx("Resources", PdfType.Resources);
            }
        }

        /// <summary>
        /// Horizontal space between cells
        /// </summary>
        public double XStep { get { return _elems.GetRealEx("XStep"); } }

        /// <summary>
        /// Vertical space between cells
        /// </summary>
        public double YStep { get { return _elems.GetRealEx("YStep"); } }

        public PdfPaintType PaintType
        {
            get
            {
                int ret = _elems.GetUIntEx("PaintType");
                if (!Enum.IsDefined(typeof(PdfPaintType), ret))
                    throw new PdfReadException(PdfType.Integer, ErrCode.OutOfRange);
                return (PdfPaintType)ret;
            }
        }

        public PdfTilingType TilingType
        {
            get
            {
                int ret = _elems.GetUIntEx("TilingType");
                if (!Enum.IsDefined(typeof(PdfTilingType), ret))
                    throw new PdfReadException(PdfType.Integer, ErrCode.OutOfRange);
                return (PdfTilingType)ret;
            }
        }

        public PdfContent Contents
        {
            get { return new PdfContent((IWStream)_content, _elems); }
        }

        #endregion

        #region Init

        public PdfTilingPattern(PdfRectangle BBox, double XStep, double YStep,
            PdfPaintType pt, PdfTilingType tl)
            : base(new TemporaryDictionary())
        {
            _elems.DirectSet("PatternType", new PdfInt(1));
            _elems.DirectSet("BBox", BBox);
            _elems.DirectSet("XStep", new PdfReal(XStep));
            _elems.DirectSet("YStep", new PdfReal(XStep));
            _elems.DirectSet("PaintType", new PdfInt((int)pt));
            _elems.DirectSet("TilingType", new PdfInt((int)tl));
            _elems.DirectSet("Resources", new PdfResources());
            _content = new WritableStream(_elems);
        }

        internal PdfTilingPattern(IWStream stream)
            : base(stream.Elements)
        {
            if (_elems.GetUIntEx("PatternType") != 1)
                throw new PdfLogException(ErrSource.Compiler, PdfType.Pattern, ErrCode.Unknown);
            _content = stream;
        }

        internal PdfTilingPattern(IWStream stream, PdfDictionary elems)
            : base(elems)
        {
            _content = stream;
        }

        /// <summary>
        /// Creates a form that can have it's contents written to.
        /// </summary>
        internal static PdfTilingPattern CreateWritable(ResTracker tracker,
            PdfRectangle BBox, xMatrix transform, double XStep, double YStep,
            PdfPaintType pt, PdfTilingType tl)
        {
            //All simple direct items can be set straight into the catalog
            var cat = new Catalog();
            cat.Add("PatternType", new PdfInt(1));
            cat.Add("XStep", new PdfReal(XStep));
            cat.Add("YStep", new PdfReal(YStep));
            cat.Add("PaintType", new PdfInt((int)pt));
            cat.Add("TilingType", new PdfInt((int)tl));

            //Implementation note:
            //The PdfResources is a set as a direct item. That means we
            //do not want it to register itself with the tracker, and we
            //achive this by creating the WritableDictionary here.
            PdfDictionary wd;
            if (tracker == null)
            {
                cat.Add("Resources", new PdfResources());
                wd = new TemporaryDictionary(cat);
            }
            else
            {
                cat.Add("Resources", new PdfResources(new WritableDictionary(tracker)));
                wd = new WritableDictionary(cat, tracker);
            }

            if (!transform.IsIdentity)
                cat.Add("Matrix", transform.ToArray());

            //However the BBox may contain references, so we let the dictionary
            //figure out what to do about that.
            wd.SetItem("BBox", BBox, false);

            return new PdfTilingPattern(new WritableStream(wd), wd);
        }

        #endregion

        #region ICStream

        void ICStream.Compress(List<FilterArray> filters, CompressionMode mode)
        {
            _content.Compress(filters, mode);
        }

        PdfDictionary ICStream.Elements { get { return _elems; } }
        void ICStream.LoadResources()
        {
            _content.LoadResources();
        }

        #endregion

        #region IERef

        protected override int IsLike(PdfPattern obj)
        {
            return (int) (Equivalent(obj) ? Equivalence.Identical : Equivalence.Different);
        }

        internal override bool Equivalent(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            if (obj is PdfTilingPattern)
            {
                var form = (PdfTilingPattern)obj;
                if (_elems.Equivalent(form._elems))
                    return PdfStream.Equals(_content, form._content);
            }
            return false;
        }

        #endregion

        #region IWPage

        void IWSPage.SetContents(byte[] contents)
        {
            SetContents(contents);
        }

        internal void SetContents(byte[] contents)
        {
            if (!(_content is NewStream))
                throw new PdfNotWritableException();
            ((NewStream)_content).DecodedStream = contents;
        }

        #endregion

        #region Required override

        internal override void Write(PdfWriter write, SM store_mode)
        {
            _content.Write(write);
        }

        internal override void Write(PdfWriter write)
        {
            throw new PdfInternalException("Tiling pattern can't be saved as a direct object");
        }

        protected override Internal.Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfTilingPattern(_content.MakeCopy(elems), elems);
        }

        protected override void DictChanged()
        {
            _content.SetDictionary(_elems);
        }

        protected override void LoadResources()
        {
            _content.LoadResources();
        }

        #endregion
    }

    public enum PdfPaintType
    {
        /// <summary>
        /// Color is specified in the content stream
        /// </summary>
        Colored = 1,

        /// <summary>
        /// Color is to be specified when the pattern is set
        /// </summary>
        Uncolored
    }

    public enum PdfTilingType
    {
        Constant = 1,
        NoDistortion,
        Fast
    }
}
