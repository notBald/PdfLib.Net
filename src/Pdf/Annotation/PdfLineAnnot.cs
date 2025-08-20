using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Annotation
{
    public sealed class PdfLineAnnot : PdfGeometricAnnot
    {
        #region Properties

        /// <summary>
        /// The starting and ending coordinates of the line in default user space.
        /// </summary>
        public xLine L 
        {
            get 
            {
                var ar = (RealArray) _elems.GetPdfTypeEx("L", PdfType.RealArray);
                if (ar.Length != 4)
                    throw new PdfReadException(PdfType.RealArray, ErrCode.Invalid);
                return new xLine(ar[0], ar[1], ar[2], ar[3]);
            }
            set
            {
                _elems.SetItemEx("L", new RealArray(new double[] { value.Start.X, value.Start.Y, value.End.X, value.End.Y }), false);
            }
        }

        public LineEndings LE
        {
            get
            {
                var ar = (NameArray) _elems.GetPdfType("LE", PdfType.NameArray);
                if (ar == null) return new LineEndings();
                if (ar.Length != 2)
                    throw new PdfReadException(PdfType.NameArray, ErrCode.Invalid);
                return new LineEndings(ToEnum(ar[0]), ToEnum(ar[1]));
            }
            set { _elems.SetItem("LE", new NameArray(new string[] { value.Start.ToString(), value.End.ToString() }), false); }
        }

        /// <summary>
        /// Leader lines
        /// </summary>
        public double LL
        {
            get { return _elems.GetReal("LL", 0); }
            set { _elems.SetReal("LL", value, 0); }
        }

        /// <summary>
        /// Leader line extension
        /// </summary>
        public double LLE
        {
            get { return Math.Max(0, _elems.GetReal("LLE", 0)); }
            set { _elems.SetReal("LLE", Math.Max(0, value), 0); }
        }

        public bool Cap
        {
            get { return _elems.GetBool("Cap", false); }
            set { _elems.SetBool("Cap", value, false); }
        }

        public double LLO
        {
            get { return _elems.GetReal("LLO", 0); }
            set { _elems.SetReal("LLO", value, 0); }
        }

        [PdfVersion("1.6")]
        public CaptionPositioning CP
        {
            get
            {
                switch (_elems.GetName("CP"))
                {
                    case "Top": return CaptionPositioning.Top;
                    default: return CaptionPositioning.Inline;
                }
            }
            set { _elems.SetName("CP", value.ToString()); }
        }

        public PdfDictionary Measure { get { return _elems.GetDictionary("Measure"); } }

        /// <summary>
        /// Offset of caption text
        /// </summary>
        public xVector CO
        {
            get
            {
                var ra = (RealArray) _elems.GetPdfType("CO", PdfType.RealArray);
                if (ra == null) return new xVector();
                if (ra.Length != 2)
                    throw new PdfReadException(PdfType.RealArray, ErrCode.Invalid);
                return new xVector(ra[0], ra[1]);
            }
            set
            {
                if (value.X == 0 && value.Y == 0)
                    _elems.Remove("CO");
                else
                    _elems.SetItem("CO", new RealArray(value.X, value.Y), false);
            }
        }

        #endregion

        #region Init

        public PdfLineAnnot(double x1, double y1, double x2, double y2)
            : this(new xLine(x1, y1, x2, y2))
        { }

        public PdfLineAnnot(xLine line)
            : base("Line", new PdfRectangle(line.Bounds))
        {
            L = line;
        }

        internal PdfLineAnnot(PdfDictionary dict)
            : base(dict)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfLineAnnot(elems);
        }

        #endregion

        #region Private

        private EndStyle ToEnum(string name)
        {
            switch (name)
            {
                case "Square": return EndStyle.Square;
                case "Circle": return EndStyle.Circle;
                case "Diamond": return EndStyle.Diamond;
                case "OpenArrow": return EndStyle.OpenArrow;
                case "ClosedArrow": return EndStyle.ClosedArrow;
                case "Butt": return EndStyle.Butt;
                case "ROpenArrow": return EndStyle.ROpenArrow;
                case "RClosedArrow": return EndStyle.RClosedArrow;
                case "Slash": return EndStyle.Slash;
                default:
                    return EndStyle.None;
            }
        }

        #endregion

        #region Helper structs

        public enum CaptionPositioning
        {
            Inline,
            Top
        }

        public struct LineEndings
        {
            public readonly EndStyle Start;
            public readonly EndStyle End;
            public LineEndings(EndStyle start, EndStyle end)
            { Start = start; End = end; }
        }

        public enum EndStyle
        {
            None,
            Square,
            Circle,
            Diamond,
            OpenArrow,
            ClosedArrow,
            Butt,
            ROpenArrow,
            RClosedArrow,
            Slash
        }

        #endregion
    }
}
