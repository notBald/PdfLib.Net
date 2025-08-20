//Todo: Does not work for "test_7c.pdf" in the AxialShaderTests folder. Though
//      this bug will only be apparent on patterns drawn with a non-90° CTM rotation
//      that has a final draw call smaller than the padding
//#define AX_LENGTH_ADJ
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Primitives;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace PdfLib.Render.WPF
{
    /// <summary>
    /// Renders patterns
    /// </summary>
    /// <remarks>
    /// Todo: Workaround for AA issue on non-axial shadings (same prinsiple
    ///       should work on function shadings at least)
    ///       
    ///       Don't use fixed number of samples (256). Be a bit more clever there.
    /// </remarks>
    internal static class rWPFPattern
    {
        public static void RenderShade(PdfShading shade, DrawingContext dc, Rect bounds)
        {
            if (shade is PdfAxialShading)
                rWPFPattern.RenderShade((PdfAxialShading)shade, dc, bounds);
            else if (shade is PdfRadialShading)
                rWPFPattern.RenderShade((PdfRadialShading)shade, dc, bounds);
            else if (shade is PdfFunctionShading)
                rWPFPattern.RenderShade((PdfFunctionShading)shade, dc, bounds);
            else if (shade is PdfPatchShading patch)
                rWPFPattern.RenderShade(patch, dc, bounds);
            else if (shade is PdfGouraudShading gour)
                rWPFPattern.RenderShade(gour, dc, bounds);
        }

        public static void RenderShade(PdfGouraudShading shade, DrawingContext dc, Rect bounds)
        {
            #region Step A - Prepwork

            //Insures the size of the visual. Function shaders only draw within the domain,
            //so this makes sure the visual is at least this big. 
            dc.DrawRectangle(Brushes.Transparent, null, bounds);

            //Clips, alternativly (and preferably) reduce the Domain to fit inside the
            //bounds when drawing.
            dc.PushClip(new RectangleGeometry(bounds));

            //Prepeares the shader for use
            var func_ar = shade.Function;
            var td = new TriangleDrawer(shade.ColorSpace, func_ar, 50);
            var triangles = shade.Triangles;

            #endregion

            foreach (var triangle in triangles)
            {
                td.Draw3ColorTriangle(dc, triangle.VA, triangle.VB, triangle.VC, triangle.C_VA, triangle.C_VB, triangle.C_VC);
            }
        }

        /// <remarks>
        /// For a good explenation for what is going on, see:
        ///  https://twinside.github.io/coon_rendering.html
        ///  
        /// This algorithem is the "naive" approach, not the more optimized variant described in
        /// that article. 
        /// </remarks>
        /// <param name="shade">Shade that will be rendered</param>
        /// <param name="dc">Render targer</param>
        /// <param name="bounds">Clip bounds</param>
        public static void RenderShade(PdfPatchShading shade, DrawingContext dc, Rect bounds)
        {
            #region Step A - Prepwork

            //Insures the size of the visual. Function shaders only draw within the domain,
            //so this makes sure the visual is at least this big. 
            dc.DrawRectangle(Brushes.Transparent, null, bounds);

            //Clips, alternativly (and preferably) reduce the Domain to fit inside the
            //bounds when drawing.
            dc.PushClip(new RectangleGeometry(bounds));

            //Prepeares the shader for use
            var func_ar = shade.Function;
            var td = new TriangleDrawer(shade.ColorSpace, func_ar);
            var tensors = shade.Tensors;

            #endregion

            foreach (var tensor in tensors)
            {
                const int NUMBER_OF_ITTERATIONS = 3; //3 = 4 times 4 times 4 splits (64 total)
                DrawTensor(dc, td, tensor, NUMBER_OF_ITTERATIONS, NUMBER_OF_ITTERATIONS);

                //DrawTensor(dc, td, tensor, 0);
            }
        }

        private static void DrawTensor(DrawingContext dc, TriangleDrawer td, PdfTensor t, int vert_splits, int hrz_splits)
        {
            PdfTensor first, last;

            SplitTensorHorizontal(t, out first, out last);

            if (hrz_splits == 0)
            {
                //DrawTensor(dc, td, first);
                //DrawTensor(dc, td, last);
                DrawTensor(dc, td, first, vert_splits);
                DrawTensor(dc, td, last, vert_splits);
            }
            else
            {
                DrawTensor(dc, td, first, vert_splits, hrz_splits - 1);
                DrawTensor(dc, td, last, vert_splits, hrz_splits - 1);
            }
        }


        private static void DrawTensor(DrawingContext dc, TriangleDrawer td, PdfTensor t, int itteration)
        {
            PdfTensor first, last;

            SplitTensorVertical(t, out first, out last);

            if (itteration == 0)
            {
                DrawTensor(dc, td, first);
                DrawTensor(dc, td, last);
            }
            else
            {
                DrawTensor(dc, td, first, itteration - 1);
                DrawTensor(dc, td, last, itteration - 1);
            }
        }

        private static void DrawTensor(DrawingContext dc, TriangleDrawer td, PdfTensor t)
        {
            td.Draw3ColorTriangle(dc, t.P00, t.P03, t.P30,
                    t.Color_ll, t.Color_ul, t.Color_lr);
            td.Draw3ColorTriangle(dc, t.P03, t.P33, t.P30,
                    t.Color_ul, t.Color_ur, t.Color_lr);
        }

        private static void SplitTensorVertical(PdfTensor tense, out PdfTensor first, out PdfTensor last)
        {
            xPoint[,] first_coords = new xPoint[4, 4], last_coords = new xPoint[4, 4];
            var coords = tense.Coords;

            //P00 -> P30
            SplitBezier(coords, 0, 0, 1, 0, first_coords, last_coords);

            //(commented out because these don't affect the end result, unless you
            // wish to split it horizontally after the vertical split)
            //P01 -> P31
            //SplitBezier(coords, 0, 1, 1, 0, first_coords, last_coords);

            //P02 -> P32
            //SplitBezier(coords, 0, 2, 1, 0, first_coords, last_coords);

            //P03 -> P33
            SplitBezier(coords, 0, 3, 1, 0, first_coords, last_coords);

            var bottom = SplitColor(tense.Color_ll, tense.Color_lr);
            var top = SplitColor(tense.Color_ul, tense.Color_ur);

            first = new PdfTensor(first_coords, tense.Color_ll, tense.Color_ul, top, bottom);
            last = new PdfTensor(last_coords, bottom, top, tense.Color_ur, tense.Color_lr);
        }

        private static void SplitTensorHorizontal(PdfTensor tense, out PdfTensor first, out PdfTensor last)
        {
            xPoint[,] first_coords = new xPoint[4, 4], last_coords = new xPoint[4, 4];
            var coords = tense.Coords;

            //P00 -> P03
            SplitBezier(coords, 0, 0, 0, 1, first_coords, last_coords);

            //P10 -> P13
            SplitBezier(coords, 1, 0, 0, 1, first_coords, last_coords);

            //P20 -> P23
            SplitBezier(coords, 2, 0, 0, 1, first_coords, last_coords);

            //P30 -> P33
            SplitBezier(coords, 3, 0, 0, 1, first_coords, last_coords);

            var left = SplitColor(tense.Color_ll, tense.Color_ul);
            var right = SplitColor(tense.Color_lr, tense.Color_ur);

            first = new PdfTensor(first_coords, tense.Color_ll, left, right, tense.Color_lr);
            last = new PdfTensor(last_coords, left, tense.Color_ul, tense.Color_ur, right);
        }

        private static double[] SplitColor(double[] first, double[] last)
        {
            var nc = new double[first.Length];
            for (int c = 0; c < nc.Length; c++)
                nc[c] = (first[c] + last[c]) / 2;

            return nc;
        }

        /// <summary>
        /// Splits a Bezier curve.
        /// </summary>
        /// <remarks>
        /// https://stackoverflow.com/questions/8369488/splitting-a-bezier-curve?noredirect=1&lq=1
        /// </remarks>
        private static void SplitBezier(xPoint[,] line, byte x, byte y, byte adv_x, byte adv_y, xPoint[,] first, xPoint[,] last)
        {
            //With midpoint at 0.5, the math can be simplified
            // (x2 - x1) * 0.5 + x1 == (x2 + x1) * 0.5
            const double t = 0.5;

            xPoint start = line[x, y];
            xPoint ctrl1 = line[x + adv_x, y + adv_y];
            xPoint ctrl2 = line[x + adv_x * 2, y + adv_y * 2];
            xPoint end = line[x + adv_x * 3, y + adv_y * 3];

            var x12 = (ctrl1.X - start.X) * t + start.X;
            var y12 = (ctrl1.Y - start.Y) * t + start.Y;

            var x23 = (ctrl2.X - ctrl1.X) * t + ctrl1.X;
            var y23 = (ctrl2.Y - ctrl1.Y) * t + ctrl1.Y;

            var x34 = (end.X - ctrl2.X) * t + ctrl2.X;
            var y34 = (end.Y - ctrl2.Y) * t + ctrl2.Y;

            var x123 = (x23 - x12) * t + x12;
            var y123 = (y23 - y12) * t + y12;

            var x234 = (x34 - x23) * t + x23;
            var y234 = (y34 - y23) * t + y23;

            var x1234 = (x234 - x123) * t + x123;
            var y1234 = (y234 - y123) * t + y123;

            first[x, y] = start;
            last[x, y] = new xPoint(x1234, y1234);
            first[x + adv_x, y + adv_y] = new xPoint(x12, y12);
            last[x + adv_x, y + adv_y] = new xPoint(x234, y234);
            first[x + adv_x * 2, y + adv_y * 2] = new xPoint(x123, y123);
            last[x + adv_x * 2, y + adv_y * 2] = new xPoint(x34, y34);
            first[x + adv_x * 3, y + adv_y * 3] = new xPoint(x1234, y1234);
            last[x + adv_x * 3, y + adv_y * 3] = end;

            //return new xPoint[] { new xPoint(x12, y12), new xPoint(x123, y123), new xPoint(x1234, y1234) };

            //[(x1,y1),(x12,y12),(x123,y123),(x1234,y1234),(x234,y234),(x34,y34),(x4,y4)] 
        }

        /// <remarks>
        /// A function shader is a two dimensional surface where functions decide the color
        /// of each point.
        /// 
        /// i.e.               Bounds rectangle
        ///       |---------------------------------------| y_max
        ///       | p11 p12 p13 ...                       |
        ///       | p21 p22                               |
        ///       | p31     p33                           |
        ///       | p41                                   |
        ///       | ...                                   |
        ///       |                                       |
        ///       |                                       |
        ///       |---------------------------------------| y_min
        ///      x_min                                   x_max
        ///      
        /// Idealy one will calcualte and paint the color of each pixel, however this impl.
        /// does not look at the pixel size and simply draws 256 squares in each direction.
        /// (for a total of 256 * 256 squares).
        /// </remarks>
        public static void RenderShade(PdfFunctionShading shade, DrawingContext dc, Rect bounds)
        {
            #region Step A - Prepwork

            //Insures the size of the visual. Function shaders only draw within the domain,
            //so this makes sure the visual is at least this big. 
            dc.DrawRectangle(Brushes.Transparent, null, bounds);

            //Clips, alternativly (and preferably) reduce the Domain to fit inside the
            //bounds when drawing.
            dc.PushClip(new RectangleGeometry(bounds));

            //Function shaders have their own coordinate system. Sets it up.
            var m = Util.XtoWPF.ToMatrix(shade.Matrix);
            dc.PushTransform(new MatrixTransform(m));

            //Moves the bounds into the shader's coordinate system.
            //m.Invert();
            //bounds.Transform(m);
            //^This can be used to improve preformance (by figuring out
            // what's not going to be drawn). But isn't needed.

            //Variables needed by the algo
            const int nSamples = 256;
            var domain = shade.Domain;
            var xmin = domain[0].Min;
            var xmax = domain[0].Max;
            var ymin = domain[1].Min;
            var ymax = domain[1].Max;
            var x_step = (xmax - xmin) / nSamples;
            var y_step = (ymax - ymin) / nSamples;
            //Note:
            //According to the specs the domain is the function's
            //internal coordinate system, which is then mapped to
            //the external coordniate system through the Maxtix.
            //
            //AFAICT Adobe does not stop drawing at the end of
            //the domain. Whereas my implementation will stop
            //drawing when x/ymax is reached. The true x/ymax
            //can be determined by looking at the bounds.

            //To prevent an eternal loop
            if (x_step <= 0 || y_step <= 0)
                return;

            //Prepeares the shader for use
            var col_conv = shade.ColorSpace.Converter;
            var funcs = shade.CreateFunctions();

            #endregion

            //This algo simply draws 256x256 squares and calls it a day. It
            //does not check if the squares is outside the bounds or anything
            //like that.
            for (double ypos = ymin; ypos < ymax; ypos += y_step)
            {
                for (double xpos = xmin; xpos < xmax; xpos += x_step)
                {
                    //Calculates the color.
                    var col = col_conv.MakeColor(funcs.GetColor(xpos, ypos));

                    //Draws the point.
                    dc.DrawRectangle(new SolidColorBrush(ToWPF(col)), null, new Rect(xpos, ypos, x_step, y_step));
                }
            }
        }

        /// <summary>
        /// Renders a radial shade
        /// </summary>
        /// <param name="shade">The radial shade</param>
        /// <param name="dc">DrawingContent</param>
        /// <param name="bounds">Size bounds</param>
        /// <remarks>
        /// WPF has a "DrawElipsis" function, but a radial shade is actually two circles
        /// being drawn together (in steps tmin to tmax) so circles are drawn using 
        /// "DrawGeometry".
        /// 
        /// Todo: Go over the code and write better comments, see if one can fix the AA issue*
        ///       and make tests that messes with sh and cm. 
        ///       
        ///       Step B - const int nSamples = 256;
        ///       Using a fixed number of samples isn't a good solution. 
        /// 
        /// * Circles are either dawn:
        ///    -Enclosed
        ///      When circles are enclosed AA can be done by either padding the inner
        ///      circle so that's it's a little larget (When drawing from large to smal)
        ///      or padd the outer circle when drawing from small to large
        ///    -Unenclosed
        ///      The padding AA compensation trick isn't truly applicable to unenclosed 
        ///      circles, as one should only pad areas that will be drawn over by the
        ///      next circle, but may be preferable to no AA anyway.
        ///      
        ///      Alternativly one may get away with drawning both the current and the
        ///      next circle (combine the geometry), as (in theory) when drawing the next
        ///      step one will simply overdraw the area that needs to be overdrawn.
        ///      
        ///      This technique, however, may look very bad when blending. Or will it? As
        ///      long as one draw onto a DV (with no blending), which again gets drawn to
        ///      the main DV with blending. Hmm. 
        ///      
        /// </remarks>
        public static void RenderShade(PdfRadialShading shade, DrawingContext dc, Rect bounds)
        {
            #region Step A - Collect data

            double xa, ya, ra;

            //The coords decides the starting point, the end point and start/end radious.
            var coords = shade.Coords;
            double x0 = coords[0];
            double y0 = coords[1];
            double r0 = coords[2]; //Non negative
            double x1 = coords[3];
            double y1 = coords[4];
            double r1 = coords[5]; //Non negative
            Debug.Assert(r0 >= 0);
            Debug.Assert(r1 >= 0);

            //The bounding box extent. Used for extending the colors of the circle to the
            //boundaries.
            double xmax = bounds.Right;
            double xmin = bounds.Left;
            if (xmax < xmin) { double tmp = xmax; xmax = xmin; xmin = tmp; }
            double ymax = bounds.Bottom;
            double ymin = bounds.Top;
            if (ymax < ymin) { double tmp = ymax; ymax = ymin; ymin = tmp; }

            //Compues the distance between the coordinate points
            double dx = x1 - x0;
            double dy = y1 - y0;
            double dr = r1 - r0;

            //I want to know the maximum and minimum radiouses
            double r_max, r_min;
            if (r1 > r0) { r_max = r1; r_min = r0; }
            else { r_max = r0; r_min = r1; }

            //The t values will be interpolated into this domain
            var domain = shade.Domain;
            double t0 = domain.Min;
            double t1 = domain.Max;
            double dt = t1 - t0;
            double tmin = t0;
            double tmax = t1;

            var exts = shade.Extend;
            var col_conv = shade.ColorSpace.Converter;

            //Prepeares the shader for use
            var funcs = shade.CreateFunctions();

            //Clipps and draws a rectangle to insure the correct size
            //when drawing onto a dv.
            dc.PushClip(new RectangleGeometry(bounds));
            dc.DrawRectangle(Brushes.Transparent, null, bounds);

            //Some formulas
            //     t - t0
            // s = -------  =>  t = s*dt + t0
            //       dt
            //
            // xc(s) = x0 + s * dx
            // yc(s) = y0 + s * dy
            // rc(s) = r0 + s * dr

            //Need to find Smin / Smax (though only when extend is in use.)
            //
            //How this is done will depend on if the two circles encloses eachother
            //or not.

            //Calcs the distance between the circles. Then adds the smaller circles
            //radious and sees if the distance of the smaler and bigger circles equals
            //
            // sqrt(dx^2 + dy^2) is the distance formula, and by adding r_min we get
            // the absolute max distance. If the biggest circle covers that area, it
            // encloses the smaller circle
            bool enclosed = Math.Sqrt(dx * dx + dy * dy) + r_min <= r_max;

            //Keeps track of if the first circle is big or not. Used when extending the
            //circles. (Big circles are extended outwards, small circles are extended invards)
            bool start_big = r0 > r1;

            #endregion

            #region Extend

            //The way I do extend is to draw the lower end (exts[0]) first and the
            //exts[1] end after the circles are drawn. The only excpetion is for enclosed
            //circles, and then it's worth nothing that the code isn't 100% correct (though
            //there's is no need to draw it later as there is never any overlap)
            if (enclosed)
            {
                //But what if the circles are the exact same size
                if (x0 == x1 && y0 == y1 && r0 == r1)
                {
                    //Adobe: [exts[0] exts[1]]
                    // [false false] Draws a thin C0/C1 circle outline*.
                    //               Inside will be transparent, t0 will be ouside with
                    //               t1 color bleeding on the outline's inside.
                    // [true false]  C1 will be filled with t0, outside (C0) is transparant
                    // [false true]  t1 will fill the BBox, C1's inside is transparant
                    // [true true]   Nothing is drawn
                    // * This thin outline is also drawn for [true false] and [false true] 
                    //   but current impl. doesn't draw any outline.

                    if (exts[0] && !exts[1])
                    {
                        var the_col = shade.ColorSpace.Converter.MakeColor(funcs.GetColor(t0));
                        dc.DrawGeometry(new SolidColorBrush(ToWPF(the_col)), null,
                            new EllipseGeometry(new Point(x1, y1), r1, r1));
                    }
                    else if (!exts[0] && exts[1])
                    {
                        GeometryGroup BBox = new GeometryGroup();
                        BBox.Children.Add(new RectangleGeometry(bounds));

                        //Does not matter which circle we add (big or small) as any error
                        //will be overdrawn.
                        BBox.Children.Add(new EllipseGeometry(new Point(x1, y1), r1, r1));

                        var the_col = shade.ColorSpace.Converter.MakeColor(funcs.GetColor(t1));
                        dc.DrawGeometry(new SolidColorBrush(ToWPF(the_col)), null, BBox);
                        //Todo:
                        // ^ If the inner circle is bigger than the bounding box, I'm guessing this
                        //   will give the wrong color. 
                    }

                    //Todo: For now I'm handeling [false false] as if it was [true true]
                    dc.Pop();
                    return;
                }
                else
                {
                    //Extends enclosed circles (that are not of the same size)
                    //At this point r0 != r1.
                    Debug.Assert(r0 != r1);

                    //Smin for the smaller end is r(s) = 0
                    //So Smin is r(Smin) = 0 => r0 + Smin * dr = 0 => Smin = -r0 / dr
                    double Smin = -r_min / dr;

                    //Then using SMin to find either tmax or tmin
                    double t_tmp = Smin * dt + t0;
                    if (t_tmp > 0) tmax += t_tmp;
                    else tmin = t_tmp;

                    //Smax is trickier, as it's defined as "when the BBox is entierly covered."

                    //This is not entierly correct. HackPaintsEnclosed_diffsize.pdf is an example
                    //of when the ext bounds don't cover the entire bounding box.
                    // ToDo: Fix this
                    if ((start_big) ? exts[0] : exts[1])
                    {
                        GeometryGroup BBox = new GeometryGroup();
                        BBox.Children.Add(new RectangleGeometry(bounds));

                        xa = x0 + t0 / dt * dx;
                        ya = y0 + t0 / dt * dy;
                        ra = r0 + t0 / dt * dr;

                        //Does not matter which circle we add (big or small) as any error
                        //will be overdrawn.
                        BBox.Children.Add(new EllipseGeometry(new Point(xa, ya), ra, ra));

                        var max_col = col_conv.MakeColor(funcs.GetColor(CalcT((start_big) ? tmin : tmax, t0, t1)));
                        dc.DrawGeometry(new SolidColorBrush(ToWPF(max_col)), null, BBox);
                    }
                    //note: The ext_big_to_bounds code may as well be moved up here. (As this is
                    //      the only place the flag is set)
                    //Also, keep in mind "HackPaintsEdgeCase.pdf"
                }
            }
            else
            {
                //Quick notes on how Adobe handles this.
                // [true false] extends Circle0 along its vector
                // [false true] extends Circle1 along its vecoor
                //Circles can grow, shrink or remain the same size.

                if (r0 == r1)
                {
                    //Case: Circles are of the same size
                    //This is "easy", just find the circle when
                    //situated on a BBox edge, and calc tmin/tmax

                    //Note: exts[1] is handeled later
                    if (exts[0])
                    {
                        //Extends the lower coords (x0, y0)

                        //Color of the lower coords is always t0 (right?)
                        var brush = new SolidColorBrush(ToWPF(col_conv.MakeColor(funcs.GetColor(t0))));
                        //double t = s1 * dt + t0;
                        //t = CalcT(t, t0, t1);

                        double s1, s2;
                        if (dy == 0)
                        {
                            //Straight line along the x axis
                            // i.e. x = bounds.Left / Right
                            //      y = y0
                            // x = x0 + s * dx => s = (x - x0) / dx
                            s1 = (bounds.Left - x0) / dx;
                            s2 = (bounds.Right - x0) / dx;
                            if (s2 < s1) { s1 = s2; }

                            //Fills extend color. I've tried setting tmax/tmin but that
                            //does not always give correct result.
                            // (tmin = s1*dt + t0)

                            //Calcs the point.
                            double xs1 = x0 + s1 * dx;
                            double ys1 = y0 + s1 * dy;
                            var rect = new Rect(new Point(x0, y0 - r0), new Point(xs1, ys1 + r0));

                            //Draws
                            dc.DrawRectangle(brush, null, rect);
                            dc.DrawEllipse(brush, null, new Point(x0, y0), r0, r0);
                        }
                        else if (dx == 0) //Straight line along the y axis
                        {
                            s1 = (bounds.Bottom - y0) / dy;
                            s2 = (bounds.Top - y0) / dy;
                            if (s2 < s1) { s1 = s2; }

                            //Calcs the point.
                            double xs1 = x0 + s1 * dx;
                            double ys1 = y0 + s1 * dy;
                            var rect = new Rect(new Point(x0 - r0, y0), new Point(xs1 + r0, ys1));

                            //Draws
                            dc.DrawRectangle(brush, null, rect);
                            dc.DrawEllipse(brush, null, new Point(x0, y0), r0, r0);
                        }
                        else
                        {
                            //Formula for a line y = mx + b
                            //Slope of the s axis
                            double slope = dy / dx;

                            //Solve b = y - m*x
                            double b = y0 - slope * x0;

                            //Calc the intersection points for top/bottom.
                            // y = slope * x + b => x = (y - y0) / slope
                            double x_top = (ymax - b) / slope;
                            double x_bottom = (ymin - b) / slope;

                            //Calc the intersection for left/right
                            // y = slope * x + b => 
                            double y_right = slope * xmax + b;
                            double y_left = slope * xmin + b;
                            if (x_top < x_bottom) { b = x_top; x_top = x_bottom; x_bottom = b; }
                            if (y_right < y_left) { b = y_left; y_left = y_right; y_right = b; }

                            //Want the most extreme point values. Technically these are "out of bounds", 
                            //but clipping handles the overdraw.

                            //Two of the values will always be "out of bounds," those are the ones
                            //we want
                            Point P_low = new Point(xmin, y_left);
                            Point P_high = new Point(xmax, y_right);
                            if (x_bottom < xmin) P_low = new Point(x_bottom, ymin);
                            if (x_top > xmax) P_high = new Point(x_top, ymax);

                            //Calculate the s values to figure out what point to use
                            //xc(s) = x0 + s * dx => s = (xc(s) - x0) / dx
                            double s_low = (P_low.X - x0) / dx;
                            double s_high = (P_high.X - x0) / dx;

                            //Mid point
                            Point P_mid = new Point(x0, y0);

                            //Draws the line
                            if (s_low < s_high)
                                dc.DrawLine(new Pen(brush, r0 * 2), P_low, P_mid);
                            else
                                dc.DrawLine(new Pen(brush, r0 * 2), P_high, P_mid);

                            //Draws the circle
                            dc.DrawEllipse(brush, null, P_mid, r0, r0);
                        }
                    }
                }
                else
                {
                    //Circles are either growing or shrinking. The
                    //shrinking circle is to be extended invards,
                    //while the growing circle is to be extended
                    //outwards.

                    //Handles the shrinking circle.
                    double Smin = -r_min / dr;
                    double t_tmp = Smin * dt + t0;
                    if (t_tmp > 0) tmax += t_tmp;
                    else tmin = t_tmp;

                    if (r1 < r0 && exts[0])
                    {
                        var brush = new SolidColorBrush(ToWPF(col_conv.MakeColor(funcs.GetColor(t0))));

                        //Quick and dirty. Simply draw ten circles and
                        //call it a day.
                        double t = -1;
                        xa = x0 + t0 / dt * dx; //t0 can be zero, so using
                        ya = y0 + t0 / dt * dy; //-1 instead. (but must draw
                        ra = r0 + t0 / dt * dr; //the first circle)
                        dc.DrawEllipse(brush, null, new Point(xa, ya), ra, ra);
                        for (int c = 0; c < 10; c++)
                        {
                            xa = x0 + t / dt * dx;
                            ya = y0 + t / dt * dy;
                            ra = r0 + t / dt * dr;
                            dc.DrawEllipse(brush, null, new Point(xa, ya), ra, ra);
                            t *= 2;
                        }

                        //Test files for this scenario:
                        // HackPaintsUnenclosed_diffsize_reverse.pdf
                        // This isn't 100% correct, but is also a rarly used feature
                        // so I'm not spending any time on it.
                    }
                }
            }
            if (!exts[0]) tmin = t0;
            if (!exts[1]) tmax = t1;

            #endregion

            #region Step B - Draw circles
            const int nSamples = 256;
            //Note that some samples may be wasted on drawing the extend areas.
            double step = (tmax - tmin) / nSamples;

            //Computes the first circle (A)
            xa = x0 + (tmin - t0) / dt * dx;
            ya = y0 + (tmin - t0) / dt * dy;
            ra = r0 + (tmin - t0) / dt * dr;

            //Caculates the first color
            var col_a = col_conv.MakeColor(funcs.GetColor(CalcT(tmin, t0, t1)));

            for (int c = 1; c <= nSamples; c++)
            {
                GeometryGroup gg = new GeometryGroup();

                //Adds the first circle
                gg.Children.Add(new EllipseGeometry(new Point(xa, ya), ra, ra));

                //Computes the next circle (B)
                double t = tmin + step * c;

                xa = x0 + (t - t0) / dt * dx;
                ya = y0 + (t - t0) / dt * dy;
                ra = r0 + (t - t0) / dt * dr;

                //Adds the second circle
                gg.Children.Add(new EllipseGeometry(new Point(xa, ya), ra, ra));

                //Paints
                dc.DrawGeometry(new SolidColorBrush(ToWPF(col_a)), null, gg);

                //Calculates the next color. Should perhaps average the two.
                col_a = col_conv.MakeColor(funcs.GetColor(CalcT(t, t0, t1)));
            }

            #endregion

            #region Extends

            if (exts[1])
            {

                if (!enclosed)
                {//Extends the upper coords (x1, y1)

                    //Color of the lower coords is always t0 (right?)
                    var brush = new SolidColorBrush(ToWPF(col_conv.MakeColor(funcs.GetColor(t1))));

                    if (r0 == r1)
                    {//The circles are not growing or shrinking.

                        if (dy == 0)
                        {
                            double s1 = (bounds.Left - x0) / dx;
                            double s2 = (bounds.Right - x0) / dx;
                            if (s2 < s1) { s2 = s1; }

                            //Calcs the point.
                            double xs2 = x0 + s2 * dx;
                            double ys2 = y0 + s2 * dy;
                            var rect = new Rect(new Point(x1, y1 - r0), new Point(xs2, ys2 + r0));

                            // Draws
                            dc.DrawRectangle(brush, null, rect);
                            dc.DrawEllipse(brush, null, new Point(x1, y1), r0, r0);
                        }
                        else if (dx == 0)
                        {
                            double s1 = (bounds.Bottom - y0) / dy;
                            double s2 = (bounds.Top - y0) / dy;
                            if (s2 < s1) { s2 = s1; }

                            //Calcs the point.
                            double xs2 = x0 + s2 * dx;
                            double ys2 = y0 + s2 * dy;
                            var rect = new Rect(new Point(x1 - r0, y1), new Point(xs2 + r0, ys2));

                            // Draws
                            dc.DrawRectangle(brush, null, rect);
                            dc.DrawEllipse(brush, null, new Point(x1, y1), r0, r0);
                        }
                        else
                        {
                            //Formula for a line y = mx + b
                            //Slope of the s axis
                            double slope = dy / dx;

                            //Solve b = y - m*x
                            double b = y0 - slope * x0;

                            //Calc the intersection points for top/bottom.
                            // y = slope * x + b => x = (y - y0) / slope
                            double x_top = (ymax - b) / slope;
                            double x_bottom = (ymin - b) / slope;

                            //Calc the intersection for left/right
                            // y = slope * x + b => 
                            double y_right = slope * xmax + b;
                            double y_left = slope * xmin + b;
                            if (x_top < x_bottom) { b = x_top; x_top = x_bottom; x_bottom = b; }
                            if (y_right < y_left) { b = y_left; y_left = y_right; y_right = b; }

                            //Want the most extreme point values. Technically these are "out of bounds", 
                            //but clipping handles the overdraw.

                            //Two of the values will always be "out of bounds," those are the ones
                            //we want
                            Point P_low = new Point(xmin, y_left);
                            Point P_high = new Point(xmax, y_right);
                            if (x_bottom < xmin) P_low = new Point(x_bottom, ymin);
                            if (x_top > xmax) P_high = new Point(x_top, ymax);

                            //Calculate the s values to figure out what point to use
                            //xc(s) = x0 + s * dx => s = (xc(s) - x0) / dx
                            double s_low = (P_low.X - x0) / dx;
                            double s_high = (P_high.X - x0) / dx;

                            //Mid point
                            Point P_mid = new Point(x1, y1);

                            //Draws the line
                            if (s_low > s_high)
                                dc.DrawLine(new Pen(brush, r0 * 2), P_low, P_mid);
                            else
                                dc.DrawLine(new Pen(brush, r0 * 2), P_high, P_mid);

                            //Draws the circle
                            dc.DrawEllipse(brush, null, P_mid, r0, r0);
                        }

                    }
                    else
                    {
                        if (r1 > r0)
                        {
                            //Quick and dirty. Simply draw ten circles and
                            //call it a day.
                            double t = t1;
                            for (int c = 0; c < 10; c++)
                            {
                                xa = x0 + t / dt * dx;
                                ya = y0 + t / dt * dy;
                                ra = r0 + t / dt * dr;

                                dc.DrawEllipse(brush, null, new Point(xa, ya), ra, ra);
                                t *= 2;
                            }

                            //Test files for this scenario:
                            // HackPaintsUnenclosed_diffsize.pdf
                            // HackPaintsUnenclosed_justUnder.pdf
                        }
                    }
                }
            }

            #endregion

            #region Cleanup


            dc.Pop();

            #endregion
        }

        /// <summary>
        /// Renders axial shading
        /// </summary>
        /// <param name="shade">The axial shading</param>
        /// <param name="dc">Drawing content to render into</param>
        /// <param name="bounds">Size of rendering</param>
        /// <param name="px_size">
        /// Pixel size. Set to 0,0 if unknown. 
        /// Only needed if rendering with AntiAlaising
        /// </param>
        /// <remarks>
        /// Problem:
        /// WPF does not allow one to implement a brush, i.e. one are stuck with the
        /// built in brushes.
        /// 
        /// Other solutions:
        ///  - Render the gradient into an image brush:
        ///    Accurate but probably quite slow. C# is not good for rendering.
        ///  - PixelShader
        ///    Fast, but pixel shaders can't progress beyond a certain complexity.
        ///    This means one will need a fallback solution.
        ///    
        /// Solutions
        /// Color sampeling. Instead of drawing each and every color transition, 
        /// make samples along the color curve. Just like with audio, more samples
        /// gives more accuracy and at some point it's "good enough".
        /// 
        /// The number of samples should perhaps be selected with an eye on the
        /// final render size, but for now I fix it at 256 (should be good enough
        /// for most usage without being "too big")
        /// 
        /// ---------------------------- Shader notes ----------------------------
        /// An axial shading is a shading along a single axis. This axis is called
        /// the t axis and can go in any direction inside a shape. For simplicities
        /// sake I define the shape to be square, regardless of what it truly is.
        /// 
        ///     Delta x (dx)
        /// |-----------------| y1                         t
        /// |               - |                 tmin ------------ tmax
        /// |             -   |
        /// |           -     |                 The t axis is a linear line. Value
        /// | t(x,y)  -       | Delta y         of t from t(x,y) is:
        /// |       -         |
        /// |     -           |                 dx * (x - x0) + dy * (y - y0)
        /// |   -             |           x' =  -----------------------------    
        /// | -               |                         dx^2 + dy^2
        /// |-----------------| y0
        /// x0    coords     x1
        ///                               x¢ = 0 £ x' £ 1 (x' is clamped)
        /// |-----------------| ymax
        /// |  Bounding box   |                t0 is the lower bounds of t
        /// |-----------------| ymin           t1 is the upper bounds of t
        /// xmin           xmax
        ///                               t = t0 + dt * x¢
        /// </remarks>
        public static void RenderShade(PdfAxialShading shade, DrawingContext dc, Rect bounds, Vector px_size)
        {
            if (bounds.IsEmpty)
                return;

            #region Step A

            //I suspect I can get away with a fixed size bounding box. 
            //Though for now I keep to the specs.

            double xmin = bounds.Left;
            double xmax = bounds.Right;
            double ymin = bounds.Top;
            double ymax = bounds.Bottom;
            //dc.DrawRectangle(Brushes.Brown, null, bounds);

            var coords = shade.Coords;
            double x0 = coords.LLx;
            double x1 = coords.URx;
            double y0 = coords.LLy;
            double y1 = coords.URy;

            var domain = shade.Domain;
            double t0 = domain.Min;
            double t1 = domain.Max;

            double tmax, tmin;

            var funcs = shade.CreateFunctions();

            #endregion

            //Testing code
            /*xmin = 0; xmax = 4;
            ymin = 0; ymax = 4;
            y0 = 1; y1 = 2;
            x0 = 0; x1 = 4;*/

            #region Step B
            //Step B calculates the largest and smalest values of t
            // (x0 and y0)

            //Computes the delta x and delta y values for the coords box.
            var dx = x1 - x0;
            var dy = y1 - y0;
            if (dx == 0 && dy == 0)
            {
                tmin = tmax = 0; //Prevents NaN

                //Pointless I think, but doesn't hurt.
                dc.DrawRectangle(Brushes.Transparent, null, bounds);
            }
            else
            {
                //Calcs the denominator of the x' formula (See method comment)
                var x_denominator = 1 / ((Math.Pow(dx, 2) + Math.Pow(dy, 2)));

                //Calculates the x' for all four cornes of the bounding box
                //to get the minimum and maximum t values.
                var temp = dx * (xmin - x0);
                tmin = (temp + dy * (ymin - y0)) * x_denominator; //LowerLeft
                temp = (temp + dy * (ymax - y0)) * x_denominator; //UpperLeft

                if (temp < tmin) { tmax = tmin; tmin = temp; }
                else tmax = temp;

                //LowerRight
                temp = (dx * (xmax - x0) + dy * (ymin - y0)) * x_denominator;

                if (temp < tmin) tmin = temp;
                else if (temp > tmax) tmax = temp;

                //UpperRight
                temp = (dx * (xmax - x0) + dy * (ymax - y0)) * x_denominator;

                if (temp < tmin) tmin = temp;
                else if (temp > tmax) tmax = temp;

                //Clamps as decided by the Extend array.
                var ext = shade.Extend;
                if (tmin < 0 && !ext.ExtendBefore) tmin = 0;
                if (tmax > 1 && !ext.ExtendAfter) tmax = 1;
                //Basically the extends array lets a shading
                //cover "the whole figure" in cases where the
                //shading don't quite cover everything.
                //Note that the color will be clamped so the
                //extended color will be "the last color".

                //Quickfix for sizing issue.
                if (!ext.ExtendBefore || !ext.ExtendAfter)
                    dc.DrawRectangle(Brushes.Transparent, null, bounds);
            }

            Debug.Assert(!double.IsNaN(tmin), "NaN");

            //Used to make the lines that makes up the single gradient step
            var clip = new AxialCrossClip(x0, y0, dx, dy, xmin, ymin, xmax, ymax);

            #endregion

            #region Workaround for AA issue
            //Todo: Investigate how this interacts with blending. 

            //I've looked into using a guideline set, but AFAICT that only works
            //when drawing vertical or horizontal lines.  

            //This approach basically pad all draws with one pixel, except for 
            //the last draw. This will hide the AA blending well enough, and
            //does seems to work (more testing wouldn't hurt). It remains to
            //be tested if there's any point of doing this. My idea is that we'll
            //get the AA we want on the next draw (since it will be blended 
            //against the former draw). At least the code is here though.

            //Using math.abs since the drawing direction is not
            //affected by what's on the CTM.

            double pad_y, pad_x;
            if (clip.Use_y_bounds)
            {
#if AX_LENGTH_ADJ
                //Note that using px_length is wrong. It can overflow the last draw call.
                //(in theory anyway, not seen it) Anyway let me experiment a bit.

                var angel = Vector.AngleBetween(px_size, new Vector(1, 1));
                if (angel % 90 != 0)
                {
                    var test_length = px_size.Length * Math.Cos(angel * Math.PI / 180);

                    pad_x = (dx < 0) ? -Math.Abs(test_length) : Math.Abs(test_length);
                }
                else
                {
                    pad_x = (dx < 0) ? -Math.Abs(px_size.Y) : Math.Abs(px_size.Y);
                }
#else
                //The angel simply tells if the pixel is rotated or non-square. Axial shader
                //tests 7 and 8 focus on rotated and non-square pixels. I've not looked too
                //deaply into this, perhaps one only need to worry if the pixel is non-square
                //(i.e. abs(px_size.X) != abs(px_size.Y), todo: figure this out 
                var angel = Vector.AngleBetween(px_size, new Vector(1, 1));
                if (angel % 90 != 0)
                    pad_x = (dx < 0) ? -Math.Abs(px_size.Length) : Math.Abs(px_size.Length);
                else
                {
                    //Square pixels should always be the same size in abs x/y directions
                    Debug.Assert(Math.Abs(px_size.X) == Math.Abs(px_size.Y));
                    pad_x = (dx < 0) ? -Math.Abs(px_size.X) : Math.Abs(px_size.X);
                }
#endif
                pad_y = 0;
            }
            else
            {
                //When lines are bounded to xmin/xmax instead of ymin/ymax
#if AX_LENGTH_ADJ
                var angel = Vector.AngleBetween(px_size, new Vector(1, 1));
                if (angel % 90 != 0)
                {
                    var test_length = px_size.Length * Math.Sin(angel * Math.PI / 180);

                    pad_y = (dy < 0) ? -Math.Abs(test_length) : Math.Abs(test_length);
                }
                else
                {
                    pad_y = (dy < 0) ? -Math.Abs(px_size.X) : Math.Abs(px_size.X);
                }
#else
                var angel = Vector.AngleBetween(px_size, new Vector(1, 1));
                if (angel % 90 != 0)
                    pad_y = (dy < 0) ? -Math.Abs(px_size.Length) : Math.Abs(px_size.Length);
                else
                    pad_y = (dy < 0) ? -Math.Abs(px_size.Y) : Math.Abs(px_size.Y);
#endif
                pad_x = 0;
            }

            #endregion

            #region Step C

            var cs = shade.ColorSpace.Converter;

            //Number of samples to make along the color curve. Higher gives more
            //accurate result, at the cost of performace.
            // todo: ImageMaskWithPattern.pdf is an example of this rez being too
            //       rough. Should use some smarts here. Perhaps adjust number of
            //       samples to the resolution?
            const int nSamples = 256;

            //Size of each step. Note that tmax - tmin means the whole range is
            //sampled. I.e. when t0 and t1 is less than tmax and tmin there will
            //be less samples for the gradient (which is only between t0 and t1)
            //      zooming the page (using CTM) will also reduce the number of
            //      samples.
            double t_step = (tmax - tmin) / nSamples;

            //first step is always tmin
            double t = tmin, t_next;

            //Calculates the first color
            var last_color = ToWPF(cs.MakeColor(funcs.GetColor(CalcT(t, t0, t1))));
            var last_edge = clip.CalcIntersectFor(t);

            for (int c = 1; c < nSamples; c++)
            {
                t_next = tmin + t_step * c;

                //Calculates the color value.
                //Perhaps use a lookup table for this. At least if color lookup 
                //turns up to be slow.
                var next_color = ToWPF(cs.MakeColor(funcs.GetColor(CalcT(t_next, t0, t1))));

                //Waits to draw until colors are different enough.
                if (!Color.AreClose(last_color, next_color))
                {
                    var e = clip.CalcIntersectFor(t_next);

                    var sg = new StreamGeometry();
                    using (var sgc = sg.Open())
                    {
                        sgc.BeginFigure(new Point(last_edge.Start.X, last_edge.Start.Y), true, true);
                        sgc.LineTo(new Point(last_edge.End.X, last_edge.End.Y), false, false);
                        sgc.LineTo(new Point(e.End.X + pad_x, e.End.Y + pad_y), false, false);
                        sgc.LineTo(new Point(e.Start.X + pad_x, e.Start.Y + pad_y), false, false);
                    }
                    sg.Freeze();


                    //byte by = (byte) c;// (byte)(255 / c);
                    //var b = new SolidColorBrush(Color.FromArgb(255, (byte)(by / 2), by, by));

                    var b = new SolidColorBrush(last_color);
                    b.Freeze();
                    dc.DrawGeometry(b, null, sg);
                    //dc.DrawRectangle(b, null, sg.Bounds);
                    //if (c == 5)
                    //dc.DrawGeometry(Brushes.Red, null, sg);

                    //Stores away values for the next loop
                    last_color = next_color;
                    t = t_next;
                    last_edge = e;
                }
            }

            //Draws the last segment without 1px padding.
            Debug.Assert(t < tmax); //<-- tmax is divided on nSamples in this impl. so this
            if (t < tmax)           //    should always be true (unless the shading is set up wrong)
            {
                var s = clip.CalcIntersectFor(t);
                var e = clip.CalcIntersectFor(tmax);

                var sg = new StreamGeometry();
                using (var sgc = sg.Open())
                {
                    sgc.BeginFigure(new Point(s.Start.X, s.Start.Y), true, true);
                    sgc.LineTo(new Point(s.End.X, s.End.Y), false, false);
                    sgc.LineTo(new Point(e.End.X, e.End.Y), false, false);
                    sgc.LineTo(new Point(e.Start.X, e.Start.Y), false, false);
                }
                sg.Freeze();

                //Don't know if last_color has been calculated, so calcking just
                //to be sure.
                last_color = ToWPF(cs.MakeColor(funcs.GetColor(CalcT(tmax, t0, t1))));
                var b = new SolidColorBrush(last_color);
                b.Freeze();
                dc.DrawGeometry(b, null, sg);
            }

            #endregion
        }

        static Color ToWPF(PdfColor c)
        {
            var col = c.ToARGB();
            return Color.FromArgb(col[0], col[1], col[2], col[3]);
        }

        static double CalcT(double t, double t0, double t1)
        {
            if (t < 0) return t0;
            else if (t > 1) return t1;
            else return t0 + (t1 - t0) * t;
        }
    }
}
