using System;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Function;
using PdfLib.Write.Internal;

namespace PdfLib.Pdf.ColorSpace.Pattern
{
    public sealed class PdfFunctionShading : PdfShading
    {
        #region Variables and properties

        /// <summary>
        /// A 2-in n-out funtion or an array of n 2-in 1-out functions,
        /// where n is the number of colorspace components
        /// </summary>
        public PdfFunctionArray Function
        {
            get
            {
                return (PdfFunctionArray)_elems.GetPdfTypeEx("Function", PdfType.FunctionArray,
                    PdfType.Array, IntMsg.NoMessage, null);
            }
            //Remeber to clear _functions if this is set.
        }

        /// <summary>
        /// The area to paint
        /// </summary>
        public xRange[] Domain
        {
            get
            {
                var ar = (RealArray)_elems.GetPdfType("Domain", PdfType.RealArray);
                if (ar == null) return new xRange[] { new xRange(0, 1), new xRange(0, 1) };
                if (ar.Length != 4) throw new PdfReadException(PdfType.RealArray, ErrCode.Invalid);
                return xRange.Create(ar);
            }
            set
            {
                _elems.SetItem("Domain", new RealArray(xRange.ToArray(value)), false);
            }
        }

        /// <summary>
        /// The coordinate mapping from the shadings domain to the target coordinate system.
        /// </summary>
        public xMatrix Matrix
        {
            get
            {
                var m = (RealArray) _elems.GetPdfType("Matrix", PdfType.RealArray);
                if (m == null) return xMatrix.Identity;
                return new xMatrix(m);
            }
            set
            {
                _elems.SetItem("Matrix", value.ToArray(), false);
            }
        }

        #endregion

        #region Init

        /// <summary>
        /// Creates a temporary shading object that a user can modify before adding it
        /// to a document
        /// </summary>
        /// <param name="cs">Color space</param>
        /// <param name="functions">functions</param>
        /// <remarks>For creating new function arrays</remarks>
        public PdfFunctionShading(IColorSpace cs, params PdfFunction[] functions)
            : base(cs, 1)
        {
            if (functions.Length == 1)
            {
                var func = functions[0];
                if (func.InputValues != 2 || func.OutputValues != cs.NComponents)
                    throw new PdfNotSupportedException("Function/Colorspace mismatch");
            }
            else
            {
                if (functions.Length != cs.NComponents)
                    throw new PdfNotSupportedException("Function/Colorspace mismatch");
                for (int c = 0; c < functions.Length; c++)
                {
                    var func = functions[c];
                    if (func.InputValues != 2 || func.OutputValues != 1)
                        throw new PdfNotSupportedException("Function/Colorspace mismatch");
                }
            }
            var funcs = new PdfItem[functions.Length];
            for (int c = 0; c < functions.Length; c++)
            {
                var func = functions[c];
                funcs[c] = (func.DefSaveMode == SM.Indirect) ? (PdfItem)(new TempReference(func, true)) : func;
            }
            _elems.SetItem("Function", new PdfFunctionArray(new SealedArray(funcs), false), false);

            if (cs is IndexedCS)
                throw new PdfNotSupportedException("Can't use Indexed colorspace with a function shader");
        }

        internal PdfFunctionShading(PdfDictionary dict)
            : base(dict) { }

        public TwoInputFunctions CreateFunctions()
        {
            return new TwoInputFunctions(Function);   
        }

        #endregion

        #region Required overrides

        protected override bool Equivalent(PdfShading obj)
        {
            var shade = (PdfFunctionShading)obj;
            return
                xRange.Compare(Domain, shade.Domain) &&
                Domain == shade.Domain &&
                Matrix == shade.Matrix &&
                Function.Equivalent(shade.Function);
        }

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfFunctionShading(elems);
        }

        #endregion
    }
}
