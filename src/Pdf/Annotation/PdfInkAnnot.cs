using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;

namespace PdfLib.Pdf.Annotation
{
    public sealed class PdfInkAnnot : PdfMarkupAnnotation
    {
        #region Variables and properties

        /// <summary>
        /// The ink list as a xPoint array
        /// </summary>
        /// <remarks>
        /// Should perhaps allow for setting the path arrays, as one may wish to
        /// reuse them.
        /// </remarks>
        public xPoint[][] InkList
        {
            get
            {
                var paths_org = _elems.GetArrayEx("InkList");
                var paths = new xPoint[paths_org.Length][];
                int i = 0;
                foreach (PdfItem path_itm in paths_org)
                {
                    var path_ar = (RealArray)path_itm.ToType(PdfType.RealArray);
                    var path = new xPoint[path_ar.Length/2];
                    for (int k = 0, j =0; k < path.Length; k++)
                    {
                        path[k++] = new xPoint(path_ar[j++], path_ar[j++]);
                    }
                    paths[i++] = path;
                }
                return paths;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("Must have paths");
                var paths_ar = new PdfItem[value.Length];
                for (int c = 0; c < paths_ar.Length; c++)
                {
                    var ar = value[c];
                    var path_ar = new PdfItem[ar.Length * 2];
                    for (int k = 0, j=0; k < ar.Length; k++)
                    {
                        path_ar[j++] = new PdfReal(ar[k].X);
                        path_ar[j++] = new PdfReal(ar[k].Y);
                    }
                    paths_ar[c] = new SealedArray(path_ar);
                }
                _elems.SetItem("InkList", new SealedArray(paths_ar), false);
            }
        }

        #endregion

        #region Init

        internal PdfInkAnnot(PdfDictionary dict)
            : base(dict)
        { }

        public PdfInkAnnot(xRect rect, xPoint[][] paths)
            : this(new PdfRectangle(rect), paths)
        { }

        public PdfInkAnnot(PdfRectangle rect, xPoint[][] paths)
            : base("Ink", rect)
        {
            InkList = paths;
        }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfInkAnnot(elems);
        }

        #endregion
    }
}
