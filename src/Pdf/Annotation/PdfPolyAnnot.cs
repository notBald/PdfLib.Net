using System;
using System.Collections.Generic;
using System.Text;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.ColorSpace;

namespace PdfLib.Pdf.Annotation
{
    public abstract class PdfPolyAnnot : PdfGeometricAnnot
    {
        #region Properties

        public xPoint[] Vertices
        {
            get
            {
                var points = (RealArray) _elems.GetPdfTypeEx("Vertices", PdfType.RealArray);
                int len = points.Length;
                var path = new xPoint[len / 2];
                for (int k = 0, j = 0; k < path.Length; k++)
                    path[k] = new xPoint(points[j++], points[j++]);
                return path;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("Must have vertices");
                var path = new PdfItem[value.Length*2];
                for (int c = 0, j = 0; c < value.Length; c++)
                {
                    var point = value[c];

                    path[j++] = new PdfReal(point.X);
                    path[j++] = new PdfReal(point.Y);
                }
                _elems.SetItem("Vertices", new RealArray(path), false);
            }
        }

        /// <summary>
        /// Intent
        /// </summary>
        [PdfVersion("1.6")]
        new public Intent IT
        {
            get
            {
                var it = _elems.GetName("IT");
                if (it == null) return Intent.Normal;
                switch (it)
                {
                    case "PolygonCloud":
                        return Intent.PolygonCloud;
                    case "PolyLineDimension":
                        return Intent.PolyLineDimension;
                    case "PolygonDimension":
                        return Intent.PolygonDimension;
                }
                return Intent.Normal;
            }
            set
            {
                switch (value)
                {
                    case Intent.PolygonCloud:
                        _elems.SetName("IT", "PolygonCloud"); break;
                    case Intent.PolyLineDimension:
                        _elems.SetName("IT", "PolyLineDimension"); break;
                    case Intent.PolygonDimension:
                        _elems.SetName("IT", "PolygonDimension"); break;
                    default:
                        _elems.Remove("IT"); break;
                }
            }
        }

        /// <summary>
        /// This is a dictionary of metadata usefull for taking meaurment of PDF document, for instance
        /// for arcitectual drawings. 
        /// </summary>
        [PdfVersion("1.7")]
        public PdfDictionary Measure
        {
            get { return _elems.GetDictionary("Measure"); }
        }

        #endregion

        #region Init

        internal PdfPolyAnnot(PdfDictionary dict)
            : base(dict)
        { }

        internal PdfPolyAnnot(string subtype, xPoint[] verticles)
            : base(subtype, MakeRect(verticles))
        { Vertices = verticles; }


        internal PdfPolyAnnot(string subtype, PdfRectangle rect, xPoint[] verticles)
            : base(subtype, rect)
        { Vertices = verticles; }

        #endregion

        static PdfRectangle MakeRect(xPoint[] verticles)
        {
            if (verticles == null || verticles.Length == 0)
                return new PdfRectangle(0, 0, 0, 0);
            double x_min = double.MaxValue, x_max = double.MinValue, 
                y_min = double.MaxValue, y_max = double.MinValue;
            foreach (var point in verticles)
            {
                x_min = Math.Min(point.X, x_min);
                x_max = Math.Max(point.X, x_max);
                y_min = Math.Min(point.Y, y_min);
                y_max = Math.Max(point.Y, y_max);
            }
            return new PdfRectangle(x_min, y_min, x_max, y_max);
        }

        public enum Intent
        {
            Normal,
            PolygonCloud,
            PolyLineDimension,
            PolygonDimension
        }
    }

    public sealed class PdfPolygonAnnot : PdfPolyAnnot
    {
        #region Init

        internal PdfPolygonAnnot(PdfDictionary dict)
            : base(dict)
        { }

        public PdfPolygonAnnot(xPoint[] verticles)
            : base("Polygon", verticles)
        { }

        public PdfPolygonAnnot(xRect rect, xPoint[] verticles)
            : base("Polygon", new PdfRectangle(rect), verticles)
        { }

        public PdfPolygonAnnot(PdfRectangle rect, xPoint[] verticles)
            : base("Polygon", rect, verticles)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfPolygonAnnot(elems);
        }

        #endregion
    }

    public sealed class PdfPolylineAnnot : PdfPolyAnnot
    {
        #region Init

        internal PdfPolylineAnnot(PdfDictionary dict)
            : base(dict)
        { }

        public PdfPolylineAnnot(xPoint[] verticles)
            : base("PolyLine", verticles)
        { }

        public PdfPolylineAnnot(xRect rect, xPoint[] verticles)
            : base("PolyLine", new PdfRectangle(rect), verticles)
        { }

        public PdfPolylineAnnot(PdfRectangle rect, xPoint[] verticles)
            : base("PolyLine", rect, verticles)
        { }

        #endregion

        #region Required overrides

        protected override Elements MakeCopy(PdfDictionary elems)
        {
            return new PdfPolylineAnnot(elems);
        }

        #endregion
    }
}
