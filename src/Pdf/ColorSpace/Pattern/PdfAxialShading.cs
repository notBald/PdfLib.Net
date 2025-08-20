using System;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Function;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    /// <summary>
    /// Could also be called PdfShadingType2
    /// </summary>
    public sealed class PdfAxialShading : PdfShading
    {
        #region Variables and properties

        /// <summary>
        /// Starting and end points of the axis, in the pattern's
        /// coordinate system
        /// </summary>
        public PdfRectangle Coords
        {
            get { return (PdfRectangle) _elems.GetPdfTypeEx("Coords", PdfType.Rectangle); }
            set { _elems.SetItem("Coords", value, false); }
        }

        /// <summary>
        /// The limiting values to parametic variable t
        /// </summary>
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
        public ExtendShade Extend
        {
            get
            {
                var ar = _elems.GetArray("Extend");
                if (ar == null) return new ExtendShade ( false, false );
                if (ar.Length != 2) throw new PdfReadException(PdfType.RealArray, ErrCode.Invalid);
                return new ExtendShade(ar.GetBoolEx(0), ar.GetBoolEx(1));
            }
            set
            {
                if (value == null)
                    _elems.Remove("Extend");
                else
                    _elems.SetItem("Extend", value.ToArray(), false);
            }
        }

        public PdfFunctionArray Function
        {
            get
            {
                return (PdfFunctionArray) _elems.GetPdfTypeEx("Function", PdfType.FunctionArray, 
                    PdfType.Array, IntMsg.NoMessage, null);
            }
        }

        #endregion

        #region Init

        /// <summary>
        /// Creates a temporary shading object that a user can modify before adding it
        /// to a document
        /// </summary>
        /// <param name="coords">The shader's start and stop point</param>
        /// <param name="cs">What colorspace to use for the shading</param>
        /// <param name="functions">Functions used to create color</param>
        public PdfAxialShading(PdfRectangle coords, IColorSpace cs, params PdfFunction[] functions)
            : base(cs, 2)
        {
            _elems.SetItem("Coords", coords, false);
            if (functions.Length == 1)
            {
                var func = functions[0];
                if (func.InputValues != 1 || func.OutputValues != cs.NComponents)
                    throw new PdfNotSupportedException("Function/Colorspace mismatch");
            }
            else
            {
                if (functions.Length != cs.NComponents)
                    throw new PdfNotSupportedException("Function/Colorspace mismatch");
                for (int c = 0; c < functions.Length; c++)
                {
                    var func = functions[c];
                    if (func.InputValues != 1 || func.OutputValues != 1)
                        throw new PdfNotSupportedException("Function/Colorspace mismatch");
                }
            }
            var funcs = new PdfItem[functions.Length];
            for (int c = 0; c < functions.Length; c++)
            {
                var func = functions[c];
                funcs[c] = (func.DefSaveMode == SM.Indirect) ? (PdfItem) (new TempReference(func)) : func;
            }
            _elems.SetItem("Function", new PdfFunctionArray(new TemporaryArray(funcs), false), false);

            if (cs is IndexedCS)
                throw new PdfNotSupportedException("Can't use Indexed colorspace");
        }

        internal PdfAxialShading(PdfDictionary dict)
            : base(dict) { }

        /// <summary>
        /// Initializes the shader for rendering.
        /// </summary>
        public SingleInputFunctions CreateFunctions()
        {
            return new SingleInputFunctions(Function);
        }

        #endregion

        #region Required overrides

        protected override bool Equivalent(PdfShading obj)
        {
            var shade = (PdfAxialShading)obj;
            return
                Coords.Equivalent(shade.Coords) &&
                Domain == shade.Domain &&
                Extend == shade.Extend &&
                Function.Equivalent(shade.Function);
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfAxialShading(elems);
        }

        #endregion
    }
}
