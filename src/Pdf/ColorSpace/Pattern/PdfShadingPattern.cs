using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    /// <summary>
    /// 8.7.4 in the specs. Could also be called PdfPatternType2
    /// </summary>
    public sealed class PdfShadingPattern : PdfPattern
    {
        #region Variables and properties

        public PdfShading Shading { get { return (PdfShading)_elems.GetPdfTypeEx("Shading", PdfType.Shading); } }

        #endregion

        #region Init

        public PdfShadingPattern(PdfShading shading)
            : base(new TemporaryDictionary())
        {
            _elems.SetInt("PatternType", 2);
            _elems.SetItem("Shading", shading, true);
        }

        internal PdfShadingPattern(PdfDictionary dict)
            : base(dict)
        {
            if (dict.GetUIntEx("PatternType") != 2)
                throw new PdfLogException(ErrSource.Compiler, PdfType.Pattern, ErrCode.Unknown);
        }

        #endregion

        #region Required override

        protected override int IsLike(PdfPattern obj)
        {
            return (int) (Equivalent(obj) ? Equivalence.Identical : Equivalence.Different );
        }

        internal override bool Equivalent(object obj)
        {
            if (obj is PdfShadingPattern)
            {
                var pat = (PdfShadingPattern)obj;
                return Shading.Equivalent(pat.Shading);
            }
            return false;
        }

        protected override Internal.Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfShadingPattern(elems);
        }

        #endregion
    }
}
