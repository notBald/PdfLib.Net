using System;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Function;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    public sealed class PdfRadialShading : PdfShading
    {
        #region Variables and properties

        /// <summary>
        /// [x0 y0 r0 x1 y1 r1]
        /// </summary>
        public double[] Coords
        {
            get 
            { 
                var ret = ((RealArray)_elems.GetPdfTypeEx("Coords", PdfType.RealArray)).ToArray();
                if (ret.Length != 6)
                    throw new PdfReadException(PdfType.RealArray, ErrCode.Invalid);
                return ret;
            }
        }

        public xRange Domain
        {
            get
            {
                var ar = (RealArray)_elems.GetPdfType("Domain", PdfType.RealArray);
                if (ar == null) return new xRange(0, 1);
                if (ar.Length != 2) throw new PdfReadException(PdfType.RealArray, ErrCode.Invalid);
                return new xRange(ar[0], ar[1]);
            }
        }

        /// <summary>
        /// The shading does not nessesarily cover the whole figure. Extends
        /// let one "streach" the shading so that it covers the figure.
        /// </summary>
        public bool[] Extend
        {
            get
            {
                var ar = _elems.GetArray("Extend");
                if (ar == null) return new bool[] { false, false };
                if (ar.Length != 2) throw new PdfReadException(PdfType.RealArray, ErrCode.Invalid);
                return new bool[] { ar.GetBoolEx(0), ar.GetBoolEx(1) };
            }
        }

        public PdfFunctionArray Function
        {
            get
            {
                return (PdfFunctionArray)_elems.GetPdfTypeEx("Function", PdfType.FunctionArray,
                    PdfType.Array, IntMsg.NoMessage, null);
            }
        }

        #endregion

        #region Init

        public PdfRadialShading(double x0, double y0, double r0, double x1, double y1, double r1, IColorSpace cs, PdfFunction func)
            : this(new double[] { x0, y0, r0, x1, y1, r1 }, cs, new PdfFunctionArray(func, false))
        { }

        internal PdfRadialShading(double[] coords, IColorSpace cs, PdfFunctionArray functions)
            : base(cs, 3)
        {
            _elems.SetNewItem("Coords", new RealArray(coords), false);
            _elems.SetItem("Function", functions, true);
        }

        internal PdfRadialShading(PdfDictionary dict)
            : base(dict) { }

        /// <summary>
        /// Initializes the shader for rendering.
        /// </summary>
        /// <remarks>
        /// If the shader is in any way changed, this data will 
        /// have to be regenerated.
        /// 
        /// For now shaders are immutable so it's not a problem.
        /// </remarks>
        public SingleInputFunctions CreateFunctions()
        {
            return new SingleInputFunctions(Function);
        }

        #endregion

        #region Required overrides

        protected override bool Equivalent(PdfShading obj)
        {
            var shade = (PdfRadialShading)obj;
            return
                Util.ArrayHelper.ArraysEqual<double>(Coords, shade.Coords) &&
                Domain == shade.Domain &&
                Util.ArrayHelper.ArraysEqual<bool>(Extend, shade.Extend) &&
                Function.Equivalent(shade.Function);
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfRadialShading(elems);
        }

        #endregion
    }
}
