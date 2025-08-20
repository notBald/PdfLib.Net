//Todo: Does not work for "test_7c.pdf" in the AxialShaderTests folder. Though
//      this bug will only be apparent on patterns drawn with a non-90° CTM rotation
//      that has a final draw call smaller than the padding
//#define AX_LENGTH_ADJ
//This is a simpler implementation that was really slow on Cairo. On the version of Cairo I got now,
//the new implementation dosn't seem to be so much faster anymore.
//#define OLD_SHADER
using PdfLib.Img.Internal;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Primitives;
using System;
using System.Diagnostics;

namespace PdfLib.Render.CairoLib
{
    /// <summary>
    /// Basically a copy/paste of rPattern, with adaptations for Cairo.
    /// 
    /// Do note that Cairo have 1 to 1 support for radial and axial shaders,
    /// and they are much faster. (But for now these shaders are not used)
    //
    /// Well, the function shader was too slow to be usable, so it has been 
    /// recoded into using the native axial shader.
    /// 
    /// The slowdown oddly enough comes from the "SetColor" method call, not the
    /// path building or any of that other stuff. Cario 1.12 is much faster at SetColor,
    /// but still so slow that the old function shader used 20 seconds to render.
    /// </summary>
    internal static class rCairoPattern
    {
        public static void RenderShade(PdfShading shade, CairoLib.Cairo dc, xRect bounds)
        {
            if (shade is PdfAxialShading)
                rCairoPattern.RenderShade((PdfAxialShading)shade, dc, bounds, new xVector());
            else if (shade is PdfRadialShading)
                rCairoPattern.RenderShade((PdfRadialShading)shade, dc, bounds);
            else if (shade is PdfFunctionShading)
                rCairoPattern.RenderShade((PdfFunctionShading)shade, dc, bounds);
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
        public static void RenderShade(PdfAxialShading shade, CairoLib.Cairo dc, xRect bounds, xVector px_size)
        {
            if (bounds.Width == 0 || bounds.Height == 0)
                return;

            #region Step A

            //I suspect I can get away with a fixed size bounding box. 
            //Though for now I keep to the specs.

            double xmin = bounds.Left;
            double xmax = bounds.Right;
            double ymin = bounds.Bottom;
            double ymax = bounds.Top;
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
            }
            else
            {
                //Calcs the denominator of the x' formula (See comment)
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
            }

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
            //be tested if there's any point of doing this (If the pattern is
            //any visibly better looking when AA is on)

            //Using math.abs since the drawing direction is not
            //affected by what's on the CTM.

            double pad_y, pad_x;
            if (clip.Use_y_bounds)
            {
#if AX_LENGTH_ADJ
                //Note that using px_length is wrong. It can overflow the last draw call.
                //(in theory anyway, not seen it) Anyway let me experiment a bit.

                var angel = xVector.AngleBetween(px_size, new xVector(1, 1));
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
                //deeply into this, perhaps one only need to worry if the pixel is non-square
                //(i.e. abs(px_size.X) != abs(px_size.Y), todo: figure this out 
                var angel = xVector.AngleBetween(px_size, new xVector(1, 1));
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
                var angel = xVector.AngleBetween(px_size, new xVector(1, 1));
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
                var angel = xVector.AngleBetween(px_size, new xVector(1, 1));
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
            var last_color = cs.MakeColor(funcs.GetColor(CalcT(t, t0, t1)));
            var last_edge = clip.CalcIntersectFor(t);

            for (int c = 1; c < nSamples; c++)
            {
                t_next = tmin + t_step * c;

                //Calculates the color value.
                //Perhaps use a lookup table for this. At least if color lookup 
                //turns up to be slow.
                var next_color = cs.MakeColor(funcs.GetColor(CalcT(t_next, t0, t1)));

                //Waits to draw until colors are different enough.
                if (!PdfColor.AreClose(last_color, next_color))
                {
                    var e = clip.CalcIntersectFor(t_next);

                    dc.MoveTo(last_edge.Start.X, last_edge.Start.Y);
                    dc.LineTo(last_edge.End.X, last_edge.End.Y);
                    dc.LineTo(e.End.X + pad_x, e.End.Y + pad_y);
                    dc.LineTo(e.Start.X + pad_x, e.Start.Y + pad_y);
                    dc.ClosePath();

                    //This is really, really slow. As in majorly.
                    //Cario has support for shadings where one set
                    //explicit color stops. I'm thinking that will
                    //be way faster.
                    //
                    //Hmm. The latest version of Cairo is 10x faster
                    //at this, seems like it can be left as is then.
                    dc.SetColor(last_color);
                    dc.Fill();

                    //Stores away values for the next loop
                    last_color = next_color;
                    t = t_next;
                    last_edge = e;
                }
            }

            //Draws the last segment without 1px padding.
            Debug.Assert(t < tmax); //<-- tmax is divided on nSamples in this impl. so this
            if (t < tmax)           //    should always be true
            {
                var s = clip.CalcIntersectFor(t);
                var e = clip.CalcIntersectFor(tmax);

                dc.MoveTo(s.Start.X, s.Start.Y);
                dc.LineTo(s.End.X, s.End.Y);
                dc.LineTo(e.End.X, e.End.Y);
                dc.LineTo(e.Start.X, e.Start.Y);
                dc.ClosePath();

                //Don't know if last_color has been calculated, so calcking just
                //to be sure.
                last_color = cs.MakeColor(funcs.GetColor(CalcT(tmax, t0, t1)));
                dc.SetColor(last_color);
                dc.Fill();
            }

            #endregion
        }

        static double CalcT(double t, double t0, double t1)
        {
            if (t < 0) return t0;
            else if (t > 1) return t1;
            else return t0 + (t1 - t0) * t;
        }

        /// <summary>
        /// Renders a radial shade
        /// </summary>
        /// <param name="shade">The radial shade</param>
        /// <param name="dc">DrawingContent</param>
        /// <param name="bounds">Size bounds</param>
        /// <remarks>
        /// This code is a bit messy and deserves a rework. Must have been late
        /// when I coded it.
        /// 
        /// Some observations that may be usefull
        ///  - Circles can: Move, stand still, grow and shrink.
        ///    - The step B code handles this fine, except when
        ///      the two circles are the exact same size and not moving
        ///      (a bug)
        ///  - The bugs/problems lay in the "Extend" code. One don't want
        ///    to keep drawing circles to the end time, so one needs to know
        ///    when to stop drawing.
        ///    -For cricles that are shrinking one can stop drawing the moment
        ///     the circle leaves the bounding box* or the moment the circle is
        ///     too small 
        ///    *That is to say when the circle fully drawn is outside the box, not
        ///     just the center. 
        ///    -For circles that are growing one need to know if it's growing faster
        ///     than it moves.
        ///     - If it moves faster one can stop drawing when it's outside the bounds
        ///     - If to moves slower one need to stop when the circle covers all four
        ///       points*
        ///     * This assumes the extent color is unchanging, as if it changes then
        ///       one must draw to the endtimes to get it right.
        ///  - One special case to consider is when a circle grows at the same speed
        ///    that it moves (or close to it). In that case the circle will eventually
        ///    grow so large that it covers a much of the bounding box as it can.
        ///     - I belive we can apporcimate this as a normal on the movment axis,
        ///       offsetting for the initial radious in the opposite direction than
        ///       that of the movment.
        /// 
        ///  - The current code cheks if circles are enclosed or not. This mearly means
        ///    that the bigger circle always encloses the smaller circle. IOW An enclosed
        ///    circle is always growing faster (or shrinking faster) than it moves. 
        ///  
        /// How are the cricles drawn:
        ///  - The circles are drawn as rings. The smaller, inner, circle is transparant
        ///   (thanks to the even_odd fillrule), so only the area between the small and big
        ///   circle is colored - i.e. a ring of color.
        /// </remarks>
        public static void RenderShade(PdfRadialShading shade, CairoLib.Cairo dc, xRect bounds)
        {
            #region Step 0 Cairo prep work

            //One can assume that this function is called withing a save/load
            //so there's no need to reset this.
            dc.FillRule = CairoLib.Cairo.Enums.FillRule.EVEN_ODD;

            #endregion

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
            double ymax = bounds.Top;
            double ymin = bounds.Bottom;
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
                        dc.SetColor(the_col);
                        dc.DrawCircle(x1, y1, r1);
                    }
                    else if (!exts[0] && exts[1])
                    {
                        var the_col = shade.ColorSpace.Converter.MakeColor(funcs.GetColor(t1));
                        dc.SetColor(the_col);

                        dc.RectangleAt(bounds);

                        //Does not matter which circle we add (big or small) as any error
                        //will be overdrawn.
                        dc.DrawCircle(x1, y1, r1);
                        //Todo:
                        // ^ If the inner circle is bigger than the bounding box, I'm guessing this
                        //   will give the wrong color. 
                    }

                    //Todo: For now I'm handeling [false false] as if it was [true true]
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
                        var max_col = col_conv.MakeColor(funcs.GetColor(CalcT((start_big) ? tmin : tmax, t0, t1)));
                        dc.SetColor(max_col);

                        dc.RectangleAt(bounds);

                        xa = x0 + t0 / dt * dx;
                        ya = y0 + t0 / dt * dy;
                        ra = r0 + t0 / dt * dr;

                        //Does not matter which circle we add (big or small) as any error
                        //will be overdrawn.
                        dc.DrawCircle(xa, ya, ra);
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
                        var brush = col_conv.MakeColor(funcs.GetColor(t0));
                        dc.SetColor(brush);
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
                            var rect = new xRect(x0, y0 - r0, xs1, ys1 + r0);

                            //Draws
                            dc.DrawRectangle(rect);
                            dc.DrawCircle(x0, y0, r0);
                        }
                        else if (dx == 0) //Straight line along the y axis
                        {
                            s1 = (bounds.Bottom - y0) / dy;
                            s2 = (bounds.Top - y0) / dy;
                            if (s2 < s1) { s1 = s2; }

                            //Calcs the point.
                            double xs1 = x0 + s1 * dx;
                            double ys1 = y0 + s1 * dy;
                            var rect = new xRect(x0 - r0, y0, xs1 + r0, ys1);

                            //Draws
                            dc.DrawRectangle(rect);
                            dc.DrawCircle(x0, y0, r0);
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
                            xPoint P_low = new xPoint(xmin, y_left);
                            xPoint P_high = new xPoint(xmax, y_right);
                            if (x_bottom < xmin) P_low = new xPoint(x_bottom, ymin);
                            if (x_top > xmax) P_high = new xPoint(x_top, ymax);

                            //Calculate the s values to figure out what point to use
                            //xc(s) = x0 + s * dx => s = (xc(s) - x0) / dx
                            double s_low = (P_low.X - x0) / dx;
                            double s_high = (P_high.X - x0) / dx;

                            //Mid point
                            xPoint P_mid = new xPoint(x0, y0);

                            //Draws the line
                            if (s_low < s_high)
                                dc.DrawLine(P_low, P_mid, r0 * 2);
                            else
                                dc.DrawLine(P_high, P_mid, r0 * 2);

                            //Draws the circle
                            dc.DrawCircle(P_mid.X, P_mid.Y, r0);
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
                        var brush = col_conv.MakeColor(funcs.GetColor(t0));
                        dc.SetColor(brush);

                        //Quick and dirty. Simply draw ten circles and
                        //call it a day.
                        double t = -1;
                        xa = x0 + t0 / dt * dx; //t0 can be zero, so using
                        ya = y0 + t0 / dt * dy; //-1 instead. (but must draw
                        ra = r0 + t0 / dt * dr; //the first circle)
                        dc.DrawCircle(xa, ya, ra);
                        for (int c = 0; c < 10; c++)
                        {
                            xa = x0 + t / dt * dx;
                            ya = y0 + t / dt * dy;
                            ra = r0 + t / dt * dr;
                            dc.DrawCircle(xa, ya, ra);
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
                //Todo: Test if the new color is similar to the
                //old color, in which case one can simply "continue"
                //to the next sample (With the possible exception of
                //the last sample drawn to make sure something gets
                //drawn). 

                dc.SetColor(col_a);

                //Adds the first circle
                dc.CircleAt(xa, ya, ra);

                //Computes the next circle (B)
                double t = tmin + step * c;

                xa = x0 + (t - t0) / dt * dx;
                ya = y0 + (t - t0) / dt * dy;
                ra = r0 + (t - t0) / dt * dr;

                //Adds the second circle and paints. Note that the paint will
                //be in the form of the area between the edge of the small circle
                //and the edge of the big circle. I.e. one draw a ring of the
                //set color.
                dc.DrawCircle(xa, ya, ra);

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
                    var brush = col_conv.MakeColor(funcs.GetColor(t1));
                    dc.SetColor(brush);

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
                            var rect = new xRect(x1, y1 - r0, xs2, ys2 + r0);

                            // Draws
                            dc.RectangleAt(rect);
                            dc.DrawCircle(x1, y1, r0);
                        }
                        else if (dx == 0)
                        {
                            double s1 = (bounds.Bottom - y0) / dy;
                            double s2 = (bounds.Top - y0) / dy;
                            if (s2 < s1) { s2 = s1; }

                            //Calcs the point.
                            double xs2 = x0 + s2 * dx;
                            double ys2 = y0 + s2 * dy;
                            var rect = new xRect(x1 - r0, y1, xs2 + r0, ys2);

                            // Draws
                            dc.RectangleAt(rect);
                            dc.DrawCircle(x1, y1, r0);
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
                            xPoint P_low = new xPoint(xmin, y_left);
                            xPoint P_high = new xPoint(xmax, y_right);
                            if (x_bottom < xmin) P_low = new xPoint(x_bottom, ymin);
                            if (x_top > xmax) P_high = new xPoint(x_top, ymax);

                            //Calculate the s values to figure out what point to use
                            //xc(s) = x0 + s * dx => s = (xc(s) - x0) / dx
                            double s_low = (P_low.X - x0) / dx;
                            double s_high = (P_high.X - x0) / dx;

                            //Mid point
                            xPoint P_mid = new xPoint(x1, y1);

                            //Draws the line
                            if (s_low > s_high)
                                dc.DrawLine(P_low, P_mid, r0 * 2);
                            else
                                dc.DrawLine(P_high, P_mid, r0 * 2);

                            //Draws the circle
                            dc.DrawCircle(P_mid.X, P_mid.Y, r0);
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

                                dc.DrawCircle(xa, ya, ra);
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


            //dc.Pop();

            #endregion
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
        public static void RenderShade(PdfFunctionShading shade, CairoLib.Cairo dc, xRect bounds)
        {
            #region Step A - Prepwork

            //Clips, alternativly (and preferably) reduce the Domain to fit inside the
            //bounds when drawing.
            dc.RectangleAt(bounds);
            dc.Clip();

            //Function shaders have their own coordinate system. Sets it up.
            dc.Transform(shade.Matrix);

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
#if OLD_SHADER
                for (double xpos = xmin; xpos < xmax; xpos += x_step)
                {
                    //Calculates the color.
                    var col = col_conv.MakeColor(funcs.GetColor(xpos, ypos));

                    //Draws the point.
                    dc.SetColor(col);
                    dc.DrawRectangle(xpos, ypos, x_step, y_step);
                }
#else
                //Calling "setColor" 256x256 times takes at least 20 seconds*, so
                //here's a simple line based approatch. A mesh pattern might be
                //faster still.
                // * though this is likely a bug in my particular cairo.dll
                var pat = new cLinearPattern(xmin, ypos, xmax, ypos);
                var map = new LinearInterpolator(xmin, xmax, 0, 1);
                for (double xpos = xmin; xpos < xmax; xpos += x_step)
                {
                    //Calculates the color.
                    var col = col_conv.MakeColor(funcs.GetColor(xpos, ypos));

                    //Cairo pattern offstets go from 0 to 1, so we map the xpos
                    //into that range.
                    var offset = map.Interpolate(xpos);

                    pat.AddColorStop(offset, col.R, col.G, col.B);
                }

                dc.SetPattern(pat);
                dc.DrawRectangle(xmin, ypos, xmax - xmin, y_step);
                pat.Dispose();
#endif
            }
        }
    }
}
