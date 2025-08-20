using System;
using System.Collections.Generic;
using PdfLib.Pdf.Internal;

namespace PdfLib.Render.CairoLib
{
    /// <summary>
    /// Contains paths and transforms.
    /// </summary>
    public class CairoGeometry
    {
        CairoPath[] _paths;
        cMatrix? _transform;

        public cMatrix? Transform
        {
            get { return _transform; }
            set
            {
                _transform = value;
            }
        }

        public CairoGeometry(CairoPath[] paths)
        {
            _paths = paths;
        }

        /// <summary>
        /// Appens this geometry to the cairo's path
        /// </summary>
        /// <param name="cr"></param>
        public void Append(Cairo cr)
        {
            for (int c = 0; c < _paths.Length; c++)
            {
                var path = _paths[c];
                var points = path.Points;

                //First we transform the points with the cPath's matrix
                if (path.Transform != null)
                    TransformPoints(points, path.Transform.Value);

                //The we transform the points with this transform
                if (Transform != null)
                    TransformPoints(points, Transform.Value);

                //Adds the path back by redrawing it. A memcopy is probably faster.
                for (int i = 0; i < points.Length; i++)
                {
                    switch (points[i].header.type)
                    {
                        case Cairo.Enums.cPathDataType.CAIRO_PATH_MOVE_TO:
                            cr.MoveTo(points[++i].point.X, points[i].point.Y);
                            break;
                        case Cairo.Enums.cPathDataType.CAIRO_PATH_LINE_TO:
                            cr.LineTo(points[++i].point.X, points[i].point.Y);
                            break;
                        case Cairo.Enums.cPathDataType.CAIRO_PATH_CURVE_TO:
                            cr.CurveTo(points[++i].point.X, points[i].point.Y,
                                       points[++i].point.X, points[i].point.Y,
                                       points[++i].point.X, points[i].point.Y);
                            break;
                        case Cairo.Enums.cPathDataType.CAIRO_PATH_CLOSE_PATH:
                            cr.ClosePath();
                            break;
                        default: i += points[i].header.length - 1;
                            break;
                    }
                }
            }
        }

        private cPath[] TransformPaths()
        {
            var paths = new cPath[_paths.Length];
            for (int c = 0; c < _paths.Length; c++)
            {
                var path = _paths[c];
                var points = path.Points;

                //First we transform the points with the cPath's matrix
                if (path.Transform != null)
                    TransformPoints(points, path.Transform.Value);

                //The we transform the points with this transform
                if (Transform != null)
                    TransformPoints(points, Transform.Value);
                
                
            }

            throw new NotImplementedException();
        }

        private void TransformPoints(cPathData[] points, cMatrix mat)
        {
            for (int c = 0; c < points.Length; c++)
            {
                int len = points[c].header.length;
                for (int i = 1; i < len; i++)
                {
                    c++;
                    points[c].point = mat.Transform(points[c].point);
                }
            }
        }
    }
}
