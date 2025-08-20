using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PdfLib.Pdf.Font;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Form;
using PdfLib.Compose;
using PdfLib.Compose.Text;
using PdfLib.Write.Internal;
using PdfLib.Render;

namespace PdfLib.Pdf.Annotation
{
    /// <summary>
    /// A free text annotation displays text directly on the page,
    /// it has no open or closed state
    /// </summary>
    /// <remarks>
    /// Issues:
    ///  - When a FreeTextAnnot is copied into another document it will lose its
    ///    reference to the owning document. This is non-trivial to fix, as one
    ///    need to insure that the referenced resources are copied into the new
    ///    document, and update the DA string with the new names for these
    ///    resources.
    ///    
    ///    For now we'll drop saving the DA string if there's anything amis with
    ///    the owner.
    /// </remarks>
    [PdfVersion("1.3")]
    public sealed class PdfFreeTextAnnot : PdfMarkupAnnotation
    {
        #region Variables and properties

        /// <summary>
        /// WThe default appearance string that shall be used in formatting 
        /// the text (see 12.7.3.3, “Variable Text”)
        /// </summary>
        /// <remarks>
        /// The specs aren't terribly clear on where resources is to be stored.
        /// I tried storing it in the AcroForms.DR (as in the specs), and the
        /// normal page resource dictionary but had no luck with that.
        /// 
        /// There's also no mention about how you can use the standar fonts
        /// directly. 
        /// </remarks>
        public string DA { get { return _elems.GetUnicodeStringEx("DA"); } }

        /// <summary>
        /// A code specifying the form of quadding (justification) 
        /// that shall be used in displaying the annotation’s text
        /// </summary>
        public Justification Q 
        { 
            get { return (Justification) _elems.GetInt("Q", 0); }
            set
            {
                if (value == Justification.Left)
                    _elems.Remove("Q");
                else _elems.SetInt("Q", (int)value);
            }
        }

        /// <summary>
        /// Rich text string
        /// </summary>
        [PdfVersion("1.5")]
        new public string RC
        {
            get
            {
                var r = _elems["RC"];
                if (r is PdfString) return r.GetString();
                if (r is IStream)
                {
                    return Read.Lexer.GetString(((IStream)r).DecodedStream);
                }
                return null;
            }
        }

        /// <summary>
        /// A default style string
        /// </summary>
        [PdfVersion("1.5")]
        public string DS { get { return _elems.GetString("DS"); } }

        /// <summary>
        /// An array of four or six numbers specifying a callout line 
        /// attached to the free text annotation.
        /// </summary>
        [PdfVersion("1.6")]
        public CalloutLine CL 
        { 
            get { return (CalloutLine) _elems.GetPdfType("CL", PdfType.CalloutLine); }
            set { _elems.SetItem("CL", value, true); }
        }

        /// <summary>
        /// A name describing the intent of the free text 
        /// annotation
        /// </summary>
        [PdfVersion("1.6")]
        new public FreeTextIntent IT
        {
            get
            {
                var r = _elems.GetString("IT");
                switch (r)
                {
                    case "FreeTextCallout": return FreeTextIntent.FreeTextCallout;
                    case "FreeTextTypeWriter": return FreeTextIntent.FreeTextTypeWriter;
                    default: return FreeTextIntent.FreeText;
                }
            }
            set
            {
                switch (value)
                {
                    case FreeTextIntent.FreeTextCallout: _elems.SetASCIIString("IT", "FreeTextCallout"); break;
                    case FreeTextIntent.FreeTextTypeWriter: _elems.SetASCIIString("IT", "FreeTextTypeWriter"); break;
                    default: _elems.Remove("IT"); break;
                }
            }
        }

        /// <summary>
        /// A border effect dictionary
        /// </summary>
        [PdfVersion("1.6")]
        public PdfDictionary BE { get { return _elems.GetDictionary("BE"); } }

        /// <summary>
        /// See page 397 in the specs
        /// </summary>
        [PdfVersion("1.6")]
        public PdfRectangle RD { get { return (PdfRectangle)_elems.GetPdfType("RD", PdfType.Rectangle); } }

        /// <summary>
        /// A border style dictionary
        /// </summary>
        [PdfVersion("1.6")]
        public PdfDictionary BS { get { return _elems.GetDictionary("BS"); } }

        /// <summary>
        ///  A name specifying the line ending style that shall be used 
        /// </summary>
        [PdfVersion("1.6")]
        public string LE
        {
            get
            {
                var r = _elems.GetString("LE");
                if (r == null) return "None";
                return r;
            }
        }

        #endregion

        #region Init

        internal PdfFreeTextAnnot(PdfDictionary dict)
            : base(dict)
        { }

        /// <summary>
        /// Creates a new text annot
        /// </summary>
        /// <param name="rect">Bounding rectangle</param>
        /// <param name="text">Text to display</param>
        /// <param name="font">Optional font</param>
        /// <param name="font_size">Font size, 0 means automatic (Must have font)</param>
        /// <param name="color">Optional color</param>
        /// <param name="owner">The document that will own this annotation</param>
        public PdfFreeTextAnnot(xRect rect, string text, string font, double font_size, PdfColor color)
            : this(new PdfRectangle(rect), text, font, font_size, color)
        { }

        /// <summary>
        /// Creates a new text annot
        /// </summary>
        /// <param name="rect">Bounding rectangle</param>
        /// <param name="text">Text to display</param>
        /// <param name="font">Optional font.</param>
        /// <param name="font_size">Font size, 0 means automatic (Must have font)</param>
        /// <param name="color">Optional color, note Adobe only accepts integer valeus (i.e. 0.5 becomes 0. Max value is still 1)</param>
        /// <param name="owner">The document that will own this annotation</param>
        public PdfFreeTextAnnot(PdfRectangle rect, string text, string font, double font_size, PdfColor color)
            : base("FreeText", rect)
        {
            Contents = text;

            InitDA(font, font_size, color);
            //_elems.SetByteString("DA", Read.Lexer.GetBytes("/Symbol 13 Tf 0 1 0 rg"));
        }

        private void InitDA(string font, double size, PdfColor color)
        {
            var sb = new StringBuilder(32);
            if (font != null)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "/{0} {1:0.###} Tf", font, size));
                if (color != null)
                    sb.Append(" ");
            }
            if (color != null)
            {
                if (color is CMYKColor)
                {
                    var k = (CMYKColor)color;
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} {3:0.###} k", k.Cyan, k.Magenta, k.Yellow, k.Black));
                }
                else if (color.IsGray)
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.###} g", color.Red));
                else
                {
                    var rgb = color.ToDblColor();
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} rg", rgb.R, rgb.G, rgb.B));
                }
            }
            _elems.SetByteString("DA", Read.Lexer.GetBytes(sb.ToString()));
        }
        

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfFreeTextAnnot(elems);
        }

        #endregion

        public enum FreeTextIntent
        {
            /// <summary>
            /// Plain annotation
            /// </summary>
            FreeText,

            /// <summary>
            /// Text annotation with callout line
            /// </summary>
            FreeTextCallout,

            /// <summary>
            /// Editable annotation.
            /// </summary>
            FreeTextTypeWriter
        }

        public class CalloutLine : ItemArray
        {
            #region Properties

            internal override PdfType Type { get { return PdfType.CalloutLine; } }

            public xPoint Start
            {
                get { return new xPoint(_items[0].GetReal(), _items[1].GetReal()); }
                set { _items.Set(0, value.X); _items.Set(1, value.Y); }
            }

            public xPoint End
            {
                get
                {
                    int off = (_items.Length == 6) ? 2 : 0;
                    return new xPoint(_items[2 + off].GetReal(), _items[3 + off].GetReal());
                }
                set
                {
                    int off = (_items.Length == 6) ? 2 : 0;
                    _items.Set(2 + off, value.X); _items.Set(3 + off, value.Y);
                }
            }

            public xPoint? Knee
            {
                get
                {
                    if (_items.Length != 6) return null;
                    return new xPoint(_items[2].GetReal(), _items[3].GetReal());
                }
                set
                {
                    if (_items.Length != 6)
                    {
                        if (value != null)
                        {
                            var end = End;
                            _items.Set(2, value.Value.X); _items.Set(3, value.Value.Y);
                            _items.Add(end.X);
                            _items.Add(end.Y);
                        }
                    }
                    else
                    {
                        if (value == null)
                        {
                            var end = End;
                            _items.Set(2, end.X); _items.Set(3, end.Y);
                            _items.Length = 4;
                        }
                        else
                        {
                            _items.Set(4, value.Value.X); _items.Set(5, value.Value.Y);
                        }
                    }
                }
            }

            #endregion

            #region Init

            public CalloutLine(xPoint start, xPoint end)
                : this(start, end, null)
            {  }

            public CalloutLine(xPoint start, xPoint end, xPoint? knee)
                : base(new TemporaryArray(4))
            {
                _items.Set(0, start.X);
                _items.Set(1, start.Y);
                if (knee != null)
                {
                    _items.Set(2, knee.Value.X);
                    _items.Set(3, knee.Value.Y);
                    _items.Length = 6;
                    _items.Set(4, end.X);
                    _items.Set(5, end.Y);
                }
                else
                {
                    _items.Set(2, end.X);
                    _items.Set(3, end.Y);
                }
            }

            internal CalloutLine(PdfArray ar)
                : base(ar)
            {
                if (ar.Length != 4 && ar.Length != 6)
                    throw new PdfReadException(PdfType.Array, ErrCode.Invalid);
            }

            #endregion

            #region Boilerplate

            protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
            {
                return new CalloutLine(array);
            }

            #endregion
        }
    }
}
