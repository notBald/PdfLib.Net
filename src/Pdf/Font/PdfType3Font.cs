using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Write.Internal;
using PdfLib.Render;
using PdfLib.Render.Font;
using PdfLib.Render.Commands;

namespace PdfLib.Pdf.Font
{
    /// <summary>
    /// Type3 fonts are basically just a font where each glyph is PdfForm
    /// like object (there Resources are shared between all glyphs).
    /// </summary>
    public class PdfType3Font : PdfFont, IPage
    {
        #region Variables and properties

        /// <summary>
        /// The character encoding of this font
        /// </summary>
        public PdfEncoding Encoding
        { get { return (PdfEncoding)_elems.GetPdfTypeEx("Encoding", PdfType.Encoding); } }
        internal override PdfItem FetchEncoding() { return Encoding; }

        /// <summary>
        /// Describes this font
        /// </summary>
        public PdfFontDescriptor FontDescriptor
        { get { return (PdfFontDescriptor)_elems.GetPdfType("FontDescriptor", PdfType.FontDescriptor); } }

        /// <summary>
        /// First character in the width array
        /// </summary>
        public int FirstChar { get { return _elems.GetUIntEx("FirstChar"); } }

        /// <summary>
        /// Last character in the width array
        /// </summary>
        public int LastChar { get { return _elems.GetUIntEx("LastChar"); } }

        /// <summary>
        /// Width if each character
        /// </summary>
        /// <remarks>
        /// Should be an indirect reference
        /// 
        /// Note that widths outside First and Last char is to be set 0.
        /// These widths are in the "FontMatrix" coordinate space, and the
        /// widths are scaled into this coordinate space using the horizontal
        /// transform (i.e. effects of rotation is ignored), todo this will 
        /// need to be tested.
        /// </remarks>
        public double[] Widths
        {
            get
            {
                var da = ((IRealArray)_elems.GetPdfTypeEx("Widths", PdfType.RealArray)).ToArray();
                if (da.Length >= 256) return da;
                var ret = new double[256];
                var fd = FontDescriptor;
                var missing_width = (fd != null) ? fd.MissingWidth : 0;
                var first = FirstChar;
                for (int c = 0; c < first; c++)
                    ret[c] = missing_width;
                Array.Copy(da, 0, ret, first, da.Length);
                for (int c = da.Length + first; c < 256; c++)
                    ret[c] = missing_width;
                return ret;
            }
        }

        /// <summary>
        /// Resources used by all glyphs inside the font
        /// </summary>
        [PdfVersion("1.2")]
        public PdfResources Resources
        {
            get
            {
                var ret = (PdfResources)_elems.GetPdfType("Resources", PdfType.Resources);

                if (ret == null)
                {
                    if (IsWritable)
                    {
                        var tracker = _elems.Tracker;
                        ret = new PdfResources(
                            tracker == null ? new TemporaryDictionary() : 
                            (PdfDictionary) new WritableDictionary(tracker));

                        //We can use SetNewItem as we know we're not overwriting anything
                        _elems.SetNewItem("Resources", ret, false);
                    }
                    else
                        ret = new PdfResources(new SealedDictionary());
                }

                return ret;
            }
        }
        internal PdfResources GetResources() { return (PdfResources)_elems.GetPdfType("Resources", PdfType.Resources); }

        /// <summary>
        /// Characters in the font
        /// </summary>
        public Type3CharProc[] CharProcs
        {
            get 
            { 
                var dict = _elems.GetDictionaryEx("CharProcs");
                var chars = new Type3CharProc[dict.Count];
                int c=0;
                foreach (var kp in dict)
                    chars[c++] = new Type3CharProc(kp.Key, kp.Value.Deref() as ICStream);
                return chars;
            }
        }

        /// <summary>
        /// Size of the glyphs
        /// </summary>
        public PdfRectangle FontBBox { get { return (PdfRectangle)_elems.GetPdfTypeEx("FontBBox", PdfType.Rectangle); } }

        /// <summary>
        /// The coordinate system inside this font.
        /// </summary>
        public xMatrix FontMatrix
        {
            get
            {
                return new xMatrix((RealArray)_elems.GetPdfTypeEx("FontMatrix", PdfType.RealArray));
            }
        }

        #endregion

        #region Init

        internal PdfType3Font(PdfDictionary dict) : base(dict) { }

        #endregion

        #region Required overrides

        protected override int IsLike(PdfFont obj)
        {
            var t1 = (PdfType3Font)obj;
            return (int)((
                PdfFile.Equivalent(Encoding, t1.Encoding) &&
                PdfFile.Equivalent(FontDescriptor, t1.FontDescriptor) &&
                Resources == t1.Resources &&
                FirstChar == t1.FirstChar &&
                LastChar == t1.LastChar &&
                Util.ArrayHelper.ArraysEqual<double>(Widths, t1.Widths) &&
                FontBBox.Equivalent(t1.FontBBox) &&
                FontMatrix == t1.FontMatrix
                //todo: PdfResources and CharProcs
                ) ? Equivalence.Identical : Equivalence.Different);
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfType3Font(elems);
        }

        #endregion

        internal Type3Font Realize(Dictionary<string, RenderCMD[]> compiled_glyphs)
        {
            //No need to use IFontFactory. No WPF dependecies here.
            return new Type3Font(Widths, FontDescriptor, Encoding, compiled_glyphs, FontBBox, FontMatrix);
        }

        protected override rFont RealizeImpl<Path>(IFontFactory<Path> factory)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Wraps a T3 glyph. This object is not in the PDF data stucture and
    /// is not unique. 
    /// </summary>
    public class Type3CharProc
    {
        /// <summary>
        /// A stream of PDF draw commands
        /// </summary>
        readonly ICStream _stream;

        /// <summary>
        /// The name of this glyph
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// A stream of PDF draw commands
        /// </summary>
        public PdfContent Contents
        {
            get { return new PdfContent((IWStream)_stream, _stream.Elements); }
        }
        internal Type3CharProc(string name, ICStream stream)
        {
            Name = name;
            if (stream == null) throw new PdfReadException(ErrSource.Font, PdfType.Stream, ErrCode.WrongType);
            _stream = stream;
        }
        public override string ToString()
        {
            return Name;
        }
    }
}
