using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace
{
    /// <summary>
    /// Pattern color space.
    /// </summary>
    /// <remarks>
    /// In a Pattern colour space, the initial colour shall be a pattern object that 
    /// causes nothing to be painted. 
    /// 
    /// This implementation is immutable
    /// </remarks>
    [PdfVersion("1.2")]
    public sealed class PatternCS : ItemArray, IColorSpace
    {
        #region Variables and properties

        private static PatternCS _pattern_cs;

        private readonly PdfColorSpaceElms _parent;

        internal override PdfType Type { get { return PdfType.ColorSpace; } }
        public PdfCSType CSType { get { return PdfCSType.Special; } }

        /// <summary>
        /// Default color for patterns are "nothing"
        /// </summary>
        public PdfColor DefaultColor { get { return null; } }

        /// <summary>
        /// The default color as an array of double values.
        /// </summary>
        public double[] DefaultColorAr { get { return null; } }

        /// <summary>
        /// Gets an instance of this colorspace
        /// </summary>
        public static PatternCS Instance
        {
            get
            {
                if (_pattern_cs == null)
                    _pattern_cs = new PatternCS();
                return _pattern_cs;
            }
        }

        /// <summary>
        /// The patterns underlying colorspace
        /// </summary>
        public IColorSpace UnderCS 
        { 
            get 
            {
                if (_items.Length == 0) return null;
                var name = _items[1].Deref();
                if (name is IColorSpace) return (IColorSpace)name;
                if (name is PdfName)
                    //Note: Will always return the same colorspace object instance.
                    return _parent.GetColorSpace(name.GetString());
                return (IColorSpace) _items.GetPdfTypeEx(1, PdfType.ColorSpace);
            } 
        }

        /// <summary>
        /// How many components the color space has
        /// </summary>
        public int NComponents { get { return 1; } }

        /// <summary>
        /// Standar decode values
        /// </summary>
        public double[] DefaultDecode
        {
            get { throw new NotSupportedException(); }
        }

        #endregion

        #region Init

        private PatternCS() : base(new SealedArray()) { _parent = null; }
        internal PatternCS(PdfArray itms, PdfColorSpaceElms parent)
            : base(itms)
        {
            if (itms.Length != 2)
                throw new PdfReadException(PdfType.ColorSpace, ErrCode.Invalid);
            var name = itms[0].Deref();
            if (!(name is PdfName) || !"Pattern".Equals(name.GetString()))
                throw new PdfReadException(PdfType.ColorSpace, ErrCode.Invalid);
            _parent = parent; 
        }

        #endregion

        #region IColorSpace

        /// <summary>
        /// Compares the color space
        /// </summary>
        bool IColorSpace.Equals(IColorSpace cs)
        {
            if (ReferenceEquals(this, cs)) return true;
            if (cs is PatternCS)
            {
                var under = UnderCS;
                if (under != null)
                    return under.Equals(((PatternCS)cs).UnderCS);
                return ((PatternCS)cs).UnderCS == null;
            }
            return false;
        }

        internal override bool Equivalent(object obj)
        {
            return ((IColorSpace) this).Equals(obj as IColorSpace);
        }

        public ColorConverter Converter
        {
            get { throw new NotSupportedException(); }
        }

        #endregion

        #region Overrides

        internal override void Write(PdfWriter write)
        {
            if (_items.Length == 0)
                write.WriteName("Pattern");
            else
                _items.Write(write);
        }

        protected override ItemArray MakeCopy(PdfArray array, ResTracker tracker)
        {
            if (array.Length == 0) return this;
            return new PatternCS(array, (PdfColorSpaceElms) tracker.MakeCopy(_parent, true));
        }

        public override string ToString()
        {
            return "/Pattern";
        }

        #endregion
    }
}
