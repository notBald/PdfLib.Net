using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Pdf.Primitives
{
    /// <summary>
    /// Used by PdfAnalyzer to track bounds. Due to rotation and sharing, bounds are not
    /// necessarily a perfect rectangular. 
    /// </summary>
    internal class xSegCollection
    {
        /// <summary>
        /// Lines int this collection. Need not be continious
        /// </summary>
        internal readonly xSegLine[] Lines;

        /// <summary>
        /// Bounds rectangle enclosing this image.
        /// </summary>
        internal readonly xRect Bounds;

        /// <summary>
        /// SegCollections known to be close to this
        /// collection
        /// </summary>
        //public List<xSegCollection> Close = null;

        /// <remarks>
        /// Since images can be rotated I want all points. It's possible to
        /// calculate the missing points if one have just ll and ur, but that's
        /// annoying.
        /// </remarks>
        internal xSegCollection(params xSegLine[] lines)
        {
            //The lines arround the image. Sorts them so
            //they come in a predictable order. 
            //(Lower leftmost -> Upper rightmost)
            Lines = lines;

            //Figures out the normalized bounds for the entire collection.
            double[] X = new double[Lines.Length * 2];
            for (int c = 0, k = 0; k < Lines.Length; k++)
            {
                var line = Lines[k];
                X[c++] = line.P1.X;
                X[c++] = line.P2.X;
            }
            Array.Sort<double>(X);
            double x = X[0], width = X[X.Length - 1];
            double[] Y = X;
            for (int c = 0, k = 0; c < Y.Length; k++)
            {
                var line = Lines[k];
                Y[c++] = line.P1.Y;
                Y[c++] = line.P2.Y;
            }
            Array.Sort<double>(Y);
            double y = Y[0], height = Y[Y.Length - 1];

            //Increases the bounds with half a pixel in all
            //directions.
            Bounds = new xRect(x - 0.5, y - 0.5, width + 1, height + 1);
        }

        public bool IsOn(xPoint p)
        {
            if (!Bounds.IsOn(p))
                return false;

            //For now we ignore rotation, sharing, etc, of a figure.
            return true;
        }

        public bool IsClose(xSegCollection seg)
        {
            if (!Bounds.IntersectsWith(seg.Bounds))
                return false;

            var lines = seg.Lines;
            for (int c = 0; c < Lines.Length; c++)
            {
                var a_line = Lines[c];

                for (int k = 0; k < lines.Length; k++)
                {
                    if (a_line.IsClose(lines[k]))
                        return true;
                }
            }

            return false;
        }

        ///// <summary>
        ///// Registers segments in the close collections
        ///// and merges them.
        ///// </summary>
        //public void AddClose(xSegCollection seg)
        //{
        //    if (Close == null)
        //    {
        //        if (seg.Close != null)
        //        {
        //            Close = seg.Close;
        //            Close.Add(this);
        //        }
        //        else
        //        {
        //            Close = new List<xSegCollection>(4);
        //            seg.Close = Close;
        //            Close.Add(this);
        //            Close.Add(seg);
        //        }
        //    }
        //    else if (seg.Close == null)
        //    {
        //        seg.Close = Close;
        //        Close.Add(seg);
        //    }
        //    else if (seg.Close != Close)
        //    {
        //        //Merges the collections
        //        var other_close = seg.Close;
        //        for (int c = 0; c < other_close.Count; c++)
        //        {
        //            var elm = other_close[c];

        //            //Since lists are shared "Close" will 
        //            //never contain the element being added
        //            //from the other list
        //            Close.Add(elm);

        //            //All shared elements are to share the same list
        //            elm.Close = Close;
        //        }
        //        Close.Add(seg);
        //    }
        //    //No need to add when seg.Close == Close 
        //    //(as it's already added)
        //}
    }

    internal class xSegLine
    {
        public readonly xPoint P1;
        public readonly xPoint P2;
        public readonly double Slope;
        public double XMax { get { return P1.X > P2.X ? P1.X : P2.X; } }
        public double YMax { get { return P1.Y > P2.Y ? P1.Y : P2.Y; } }
        public double XMin { get { return P1.X < P2.X ? P1.X : P2.X; } }
        public double YMin { get { return P1.Y < P2.Y ? P1.Y : P2.Y; } }
        public xSegLine(xPoint p1, xPoint p2)
        {
            P1 = p1; P2 = p2;

            //Slope formula:                    Line formula
            //
            //    (x2,y2)       (y1 - y2)
            //     /        m = ---------       y = mx + b
            //    /             (x1 - x2)
            // (x1,y1)

            //Order this is done isn't important, only that
            //it's consistent between X and Y
            double x = p1.X - p2.X;

            //It does not matter whenever the slope goes down
            //ot up, so insure that -1/0 and 1/0 gives the
            //same value. 
            Slope = (x == 0) ? Double.PositiveInfinity : (p1.Y - p2.Y) / x;
        }

        internal bool IsClose(xSegLine line)
        {
            double dy;

            if (Slope != line.Slope)
                return false;

            if (Slope == double.PositiveInfinity)
            {
                //We have vertical lines. I.e. only
                //the x axis is interesing.
                var dx = P1.X - line.P1.X;
                if (dx > 1 || dx < -1) return false;

                //Final check to see if there's any overlap
                double ymax1 = YMax, ymax2 = line.YMax;
                double ymin1 = YMin, ymin2 = line.YMin;
                if (ymax1 < ymin2)
                {
                    if (ymin2 - ymax1 > 1)
                        return false;
                }
                else if (ymin1 > ymax2)
                {
                    if (ymin1 - ymax2 > 1)
                        return false;
                }

                return true;
            }

            if (Slope == 0)
            {
                //We have a horizontal line. I.e. only the y
                //axis is interesting
                dy = P1.Y - line.P1.Y;
                if (dy > 1 || dy < -1) return false;

                //Final check to see if there's any overlap
                double xmax1 = XMax, xmax2 = line.XMax;
                double xmin1 = XMin, xmin2 = line.XMin;
                if (xmax1 < xmin2)
                {
                    if (xmin2 - xmax1 > 1)
                        return false;
                }
                else if (xmin1 > xmax2)
                {
                    if (xmin1 - xmax2 > 1)
                        return false;
                }

                return true;
            }

            //First check if they are close on the y axis. This
            //is done by solving the line formula for b, and comparing
            //them.
            // b = y - mx;
            var b1 = P1.Y - Slope * P1.X;
            var b2 = line.P1.Y - line.Slope * line.P1.X;
            var db = b1 - b2;
            if (db > 1 || db < -1) return false;

            //Now we check if the lines are close on the x axis.
            //This is done by plugging y=0 into the now know line
            //formula.
            // x = (b - y) / m
            var y1 = 1 / Slope;
            var y2 = 1 / line.Slope;
            dy = y1 - y2;
            if (dy > 1 || dy < -1) return false;

            //Todo: Final check. See if there's any overlap

            return true;
        }
    }
}
