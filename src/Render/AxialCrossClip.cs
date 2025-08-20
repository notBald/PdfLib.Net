using PdfLib.Pdf.Primitives;
using System;

namespace PdfLib.Render
{
    // Assumption: That Coords are inside the bounds. Some testing should be
    //             done on this potentially troublesome issue as it's a blind
    //             assumption.
    //
    // The bounding box can be viewed as four "infinite" lines. The t axis
    // passes through the bounding box. What we want is to know the polygon
    // metrix for the space between two points on the t axis. (I.e. the shape
    // of the polygon to draw)
    //
    // Consider one point:
    //
    //     Delta x (dx)
    // *-----------------| y1         A line that's pendicular to the t axis
    // |  *            - |            passes through a point on the line. 
    // |     *       -   |
    // |        *  -     |            Depending on how it is sloped the 
    // | t(x,y)  - *     | Delta y    pendicular line will cross the four lines
    // |       -      *  |            that make up the bounding box in up to 
    // |     -           *            four locations.
    // |   -             |  *         
    // | -               |     *      We want to find the two locations that 
    // |-----------------|--------*   are on the bounding box. 
    // x0    coords*    x1
    //
    //
    // Notation:
    //                     Bounds rectangle
    //        |---------------------------------------| y_max
    //        |            Coords rectangle           |
    //        |- - - - - - - - - - - - - - - - - - - -| y1
    //        |                             -------   | |
    //   ty~ ~ ~ ~ ~ ~ ~ ~ ~ ~ ---t---                | |- Delta y (dy)
    //    |   |  ------           |                   | |
    //    |   |- - - - - - - - - -|- - - - - - - - - -| y0
    //    |   |                   |                   |
    //    |   |-------------------|-------------------| y_min
    //    |  x_min                |                 x_max
    //   0.0---------------------tx
    class AxialCrossClip
    {
        readonly double x0, y0, dx, dy, xmin, ymin, xmax, ymax;
        public readonly bool Use_y_bounds;

        /// <summary>
        /// The pendicular slope to t
        /// </summary>
        readonly double pt_slope;

        internal AxialCrossClip(double x0, double y0, double dx, double dy,
                                double xmin, double ymin, double xmax, double ymax)
        {
            this.x0 = x0; this.y0 = y0; this.dx = dx; this.dy = dy;
            this.xmin = xmin; this.ymin = ymin; this.xmax = xmax; this.ymax = ymax;


            //Calc slope of the t line. This is done using the slope
            //formula:
            //      x1 - x2 (dx)
            //  m = -------
            //      y1 - y2 (dy)
            //
            // Where x_min, y0 and x_max, y1 are two known points on the line
            //
            // Then divides -1 / result, giving the pendicular slope
            if (dy != 0)
                pt_slope = -dx / dy;

            //Very long lines may cause problems for the WPF line drawing*,
            //so use the edges that will give the shortest line
            // * I don't know, but it does in Java 1.4
            //(Also, if a line is paralell with one axis it will never cross
            // the other, so we need to do this anyway.)
            Use_y_bounds = Math.Abs(dx) > Math.Abs(dy);
        }

        internal xLine CalcIntersectFor(double t)
        {
            // the componet values of t(x, y) is found by multiplying the t value
            // with delta x (for tx) and delta y (for ty). Offseting with x0 and y0
            // (aligning them with point 0.0)
            var tx = t * dx + x0;
            var ty = t * dy + y0;

            //Special casing zero. The problem is that 0/0 and 0/-0 spits out NaN
            //values. 
            if (dx == 0)
            {
                //I suppose having both dx and dy zero is pointless, but it is 
                //possible/legal to set the coords up in such a way.
                if (dy == 0)
                    return new xLine(tx, ty, tx, ty);
                {
                    //Line is vertical, pline is horizontal
                    return new xLine(xmin, ty, xmax, ty);
                }
            }
            else if (dy == 0)
            {
                //Line is horizontal, pline is vertical
                return new xLine(tx, ymin, tx, ymax);
            }
            else
            {
               
                //          * Pendicular line (90 degrees on the t axis)
                //        |-----------*---------------------------| y_max
                //        |             *                         |
                //        |- - - - - - - -*- - - - - - - - - - - -| y1
                //        |                 *           -------   | |
                //       ty~ ~ ~ ~ ~ ~ ~ ~ ---t---                | |- Delta y (dy)
                //        |  ------           | *                 | |
                //        |- - - - - - - - - -|- -*- - - - - - - -| y0
                //        |                   |     *             |
                //        |------------------tx-------*-----------| y_min
                //       x_min                                   x_max
                //
                //Line formula y = mx + b
                //
                //m is the slope, and has already been calculated in the
                //constructor (pt_slope). 
                //
                //Finds the missing variable from the line formula
                //by doing: b = y - mx. We input the known point (tx, ty)
                //to get b
                var b = ty - pt_slope * tx;

                //Then we find the "on" points by taking the x/y values
                //for the bounding box and pluging them into the line 
                //formula.
                if (Use_y_bounds)
                {
                    var xymin = (ymin - b) / pt_slope;
                    var xymax = (ymax - b) / pt_slope;

                    //The points are selected so to avoid non-rectangular 
                    //issues (see next comment)
                    return new xLine(xymin, ymin, xymax, ymax);
                }
                else
                {
                    var yxmin = pt_slope * xmin + b;
                    var yxmax = pt_slope * xmax + b;

                    return new xLine(xmin, yxmin, xmax, yxmax);
                }

                //^Note that I always pick the same points, but this 
                // isn't always right. Read all about it below. 
                //
                // Take this example:
                //          * Pendicular line (90 degrees on the t axis)
                //  ---*--|-----------*---------------------------| y_max
                //       *|             *                         |
                //        |* - - - - - - -*- - - - - - - - - - - -| y1
                //        |  *              *           -------   | |
                //        |    *           ---t---                | |- Delta y (dy)
                //        |  ----*--          | *                 | |
                //        |- - - - * - - - - -|- -*- - - - - - - -| y0
                //        |          *        |     *             |
                //        |------------*-----tx-------*-----------| y_min
                //       x_min           *                        x_max
                //
                // The left pendicular line crosses x_min, instead of 
                // y_max. But I will take y_max regardless.
                //
                // This is done to simplify the geometry, as otherwise
                // one will have to intersect the lines with the bounding
                // box to get the geometry to draw.
                //
                // This bug is however hidden by clipping, so it has
                // no visual effect
                // 
            }
        }
    }

    class AxialCrossClipF
    {
        readonly float x0, y0, dx, dy, xmin, ymin, xmax, ymax;
        public readonly bool Use_y_bounds;

        /// <summary>
        /// The pendicular slope to t
        /// </summary>
        readonly float pt_slope;

        internal AxialCrossClipF(float x0, float y0, float dx, float dy,
                                float xmin, float ymin, float xmax, float ymax)
        {
            this.x0 = x0; this.y0 = y0; this.dx = dx; this.dy = dy;
            this.xmin = xmin; this.ymin = ymin; this.xmax = xmax; this.ymax = ymax;


            //Calc slope of the t line. This is done using the slope
            //formula:
            //      x1 - x2 (dx)
            //  m = -------
            //      y1 - y2 (dy)
            //
            // Where x_min, y0 and x_max, y1 are two known points on the line
            //
            // Then divides -1 / result, giving the pendicular slope
            if (dy != 0)
                pt_slope = -dx / dy; //<-- This is equivalent with the -1 / result formula

            //Very long lines may cause problems for the WPF line drawing*,
            //so use the edges that will give the shortest line
            // * I don't know, but it does in Java 1.4
            //(Also, if a line is paralell with one axis it will never cross
            // the other, so we need to do this anyway.)
            Use_y_bounds = Math.Abs(dx) > Math.Abs(dy);
        }

        internal xLineF CalcIntersectFor(float t)
        {
            // the componet values of t(x, y) is found by multiplying the t value
            // with delta x (for tx) and delta y (for ty). Offseting with x0 and y0
            // (aligning them with point 0.0)
            var tx = t * dx + x0;
            var ty = t * dy + y0;

            //Special casing zero. The problem is that 0/0 and 0/-0 spits out NaN
            //values. 
            if (dx == 0)
            {
                //I suppose having both dx and dy zero is pointless, but it is 
                //possible/legal to set the coords up in such a way.
                if (dy == 0)
                    return new xLineF(tx, ty, tx, ty);
                {
                    //Line is vertical, pline is horizontal
                    return new xLineF(xmin, ty, xmax, ty);
                }
            }
            else if (dy == 0)
            {
                //Line is horizontal, pline is vertical
                return new xLineF(tx, ymin, tx, ymax);
            }
            else
            {

                //          * Pendicular line (90 degrees on the t axis)
                //        |-----------*---------------------------| y_max
                //        |             *                         |
                //        |- - - - - - - -*- - - - - - - - - - - -| y1
                //        |                 *           -------   | |
                //       ty~ ~ ~ ~ ~ ~ ~ ~ ---t---                | |- Delta y (dy)
                //        |  ------           | *                 | |
                //        |- - - - - - - - - -|- -*- - - - - - - -| y0
                //        |                   |     *             |
                //        |------------------tx-------*-----------| y_min
                //       x_min                                   x_max
                //
                //Line formula y = mx + b
                //
                //m is the slope, and has already been calculated in the
                //constructor (pt_slope). 
                //
                //Finds the missing variable from the line formula
                //by doing: b = y - mx. We input the known point (tx, ty)
                //to get b
                var b = ty - pt_slope * tx;

                //Then we find the "on" points by taking the x/y values
                //for the bounding box and pluging them into the line 
                //formula.
                if (Use_y_bounds)
                {
                    var xymin = (ymin - b) / pt_slope;
                    var xymax = (ymax - b) / pt_slope;

                    //The points are selected so to avoid non-rectangular 
                    //issues (see next comment)
                    return new xLineF(xymin, ymin, xymax, ymax);
                }
                else
                {
                    var yxmin = pt_slope * xmin + b;
                    var yxmax = pt_slope * xmax + b;

                    return new xLineF(xmin, yxmin, xmax, yxmax);
                }

                //^Note that I always pick the same points, but this 
                // isn't always right. Read all about it below. 
                //
                // Take this example:
                //          * Pendicular line (90 degrees on the t axis)
                //  ---*--|-----------*---------------------------| y_max
                //       *|             *                         |
                //        |* - - - - - - -*- - - - - - - - - - - -| y1
                //        |  *              *           -------   | |
                //        |    *           ---t---                | |- Delta y (dy)
                //        |  ----*--          | *                 | |
                //        |- - - - * - - - - -|- -*- - - - - - - -| y0
                //        |          *        |     *             |
                //        |------------*-----tx-------*-----------| y_min
                //       x_min           *                        x_max
                //
                // The left pendicular line crosses x_min, instead of 
                // y_max. But I will take y_max regardless.
                //
                // This is done to simplify the geometry, as otherwise
                // one will have to intersect the lines with the bounding
                // box to get the geometry to draw.
                //
                // This bug is however hidden by clipping, so it has
                // no visual effect
                // 
            }
        }
    }
}
