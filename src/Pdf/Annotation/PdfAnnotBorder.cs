using System;
using System.Collections.Generic;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.Annotation
{
    public sealed class PdfAnnotBorder : ItemArray
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.AnnotBorder; } }

        /// <summary>
        /// If this is the default annot border.
        /// </summary>
        internal bool IsDefault
        {
            get
            {
                if (_items.Length < 3 || _items.Length > 4) return false;
                var def = HorizontalRadius == 0 && VerticalRadius == 0 && BorderWidth == 1;
                if (def && _items.Length >= 4)
                    return Dashstyle.Value.Dashes.Length == 0;
                return def;
            }
        }

        /// <summary>
        /// Horizontal border radius. Note, VerticalRadius must be set to have a visible effect.
        /// </summary>
        public double HorizontalRadius
        {
            get { return _items[0].GetReal(); }
            set { _items[0] = new PdfReal(value); }
        }

        /// <summary>
        /// Vertical border radius. Note, HorizontalRadius must be set to have a visible effect.
        /// </summary>
        public double VerticalRadius
        {
            get { return _items[1].GetReal(); }
            set { _items[1] = new PdfReal(value); }
        }

        /// <summary>
        /// Thickness of the border. Note, the figure
        /// is shrunk down by half the border width.
        /// </summary>
        public double BorderWidth
        {
            get { return _items[2].GetReal(); }
            set { _items[2] = new PdfReal(value); }
        }

        /// <summary>
        /// The dash style. Note that phase is ignored and
        /// that Adobe also ignores the second value, and
        /// treats the first as "gap". I.e [6 6] is more like
        /// [3 6].
        /// </summary>
        public xDashStyle? Dashstyle
        {
            get 
            {
                if (_items.Length < 4)
                    return null;
                var ra = (RealArray) _items.GetPdfTypeEx(3, PdfType.RealArray);
                return new xDashStyle(0, ra.ToArray());
            }
            set
            {
                if (value == null)
                    _items.Remove(3);
                var da = new RealArray(value.Value.Dashes);
                if (_items.Length < 4)
                    _items.AddItem(da);
                else
                    _items[3] = da;
            }
        }

        #endregion

        #region Init

        internal PdfAnnotBorder(PdfArray ar)
            : base(ar)
        {
            if (ar.Length < 3)
                throw new PdfReadException(PdfType.Array, ErrCode.Invalid);
        }

        internal PdfAnnotBorder(bool writable, ResTracker tracker)
            : this(writable, tracker, new PdfItem[3])
        { }

        private PdfAnnotBorder(bool writable, ResTracker tracker, PdfItem[] ar)
            : base(writable, tracker, ar)
        {
            ar[0] = new PdfInt(0);
            ar[1] = new PdfInt(0);
            ar[2] = new PdfInt(1);
        }

        public PdfAnnotBorder()
            : base(new TemporaryArray(3))
        {
            _items.Set(0, 0);
            _items.Set(1, 0);
            _items.Set(2, 1);
        }

        #endregion

        #region Required overrides

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            return new PdfAnnotBorder(array);
        }

        #endregion
    }

    public sealed class PdfBorderEffect : Elements
    {
        #region Variables and properties

        internal override PdfType Type { get { return PdfType.BorderEffect; }  }

        /// <summary>
        /// What border effect to apply
        /// </summary>
        public Effect S
        {
            get
            {
                var s = _elems.GetName("S");
                if (s == null || s != "C") return Effect.Normal;
                return Effect.Cloudy;
            }
            set
            {
                if (value == Effect.Normal)
                    _elems.Remove("S");
                else
                    _elems.SetName("S", "C");
            }
        }

        /// <summary>
        /// The intensity of the effect, ranging from 0 to 2
        /// </summary>
        public double I
        {
            get { return _elems.GetReal("I", 0); }
            set
            {
                var i = Math.Min(2, Math.Max(0, I));
                if (Util.Real.IsZero(value))
                    _elems.Remove("I");
                else
                    _elems.SetReal("I", value);
            }
        }

        #endregion

        #region Init

        internal PdfBorderEffect(PdfDictionary dict)
            : base(dict)
        { }

        public PdfBorderEffect(Effect effect, double intensity)
            : base(new TemporaryDictionary())
        {
            I = intensity;
            S = effect;
        }

        #endregion

        #region Boilerplate

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfBorderEffect(elems);
        }

        #endregion

        public enum Effect
        {
            Normal,
            Cloudy
        }
    }
}
