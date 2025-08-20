//Todo: Does not work for "test_7c.pdf" in the AxialShaderTests folder. Though
//      this bug will only be apparent on patterns drawn with a non-90° CTM rotation
//      that has a final draw call smaller than the padding
//#define AX_LENGTH_ADJ
using PdfLib.Img.Internal;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Function;
using System;
using System.Diagnostics;

namespace PdfLib.Render
{
    public abstract class rPattern
    {
        public void RenderShade(PdfShading shade, xRectF bounds)
        {
            if (shade is PdfAxialShading ax)
                RenderShade(ax, bounds, new xVectorF());
            else if (shade is PdfRadialShading rs)
                RenderShade(rs, bounds);
            else if (shade is PdfFunctionShading fs)
                RenderShade(fs, bounds);
            else if (shade is PdfGouraudShading gour)
                RenderShade(gour, bounds);
            else if (shade is PdfPatchShading patch)
                RenderShade(patch,  bounds);
        }

        public void RenderShade(PdfAxialShading shade, xRectF bounds, xVectorF px_size)
        {
            if (bounds.IsEmpty)
                return;

            #region Step A - Fetch data

            float xmin = bounds.Left;
            float xmax = bounds.Right;
            float ymin = bounds.Bottom;
            float ymax = bounds.Top;

            var coords = shade.Coords;
            float x0 = (float)coords.LLx;
            float x1 = (float)coords.URx;
            float y0 = (float)coords.LLy;
            float y1 = (float)coords.URy;

            var domain = shade.Domain;
            float t0 = (float)domain.Min;
            float t1 = (float)domain.Max;

            float tmax, tmin;

            var funcs = shade.CreateFunctions();

            #endregion

            #region Step B - Calculate sizes
            //Step B calculates the largest and smalest values of t
            // (x0 and y0)

            //Computes the delta x and delta y values for the coords box.
            float dx = x1 - x0;
            float dy = y1 - y0;
            if (dx == 0 && dy == 0)
            {
                tmin = tmax = 0; //Prevents NaN
            }
            else
            {
                //Calcs the denominator of the x' formula (See comment)
                float x_denominator = (float)(1f / ((Math.Pow(dx, 2) + Math.Pow(dy, 2))));

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
            var clip = new AxialCrossClipF(x0, y0, dx, dy, xmin, ymin, xmax, ymax);

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

            float pad_y, pad_x;
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
                var angel = xVectorF.AngleBetween(px_size, new xVectorF(1, 1));
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
                var angel = xVectorF.AngleBetween(px_size, new xVectorF(1, 1));
                if (angel % 90 != 0)
                    pad_y = (dy < 0) ? -Math.Abs(px_size.Length) : Math.Abs(px_size.Length);
                else
                    pad_y = (dy < 0) ? -Math.Abs(px_size.Y) : Math.Abs(px_size.Y);
#endif
                pad_x = 0;
            }

            #endregion

            #region Step C - Paint

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
            float t_step = (tmax - tmin) / nSamples;

            //first step is always tmin
            float t = tmin, t_next;

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

                    MoveTo(last_edge.Start.X, last_edge.Start.Y);
                    LineTo(last_edge.End.X, last_edge.End.Y);
                    LineTo(e.End.X + pad_x, e.End.Y + pad_y);
                    LineTo(e.Start.X + pad_x, e.Start.Y + pad_y);
                    ClosePath();

                    //This is really, really slow. As in majorly.
                    //Cario has support for shadings where one set
                    //explicit color stops. I'm thinking that will
                    //be way faster.
                    //
                    //Hmm. The latest version of Cairo is 10x faster
                    //at this, seems like it can be left as is then.
                    SetColor(last_color);
                    Fill();

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

                MoveTo(s.Start.X, s.Start.Y);
                LineTo(s.End.X, s.End.Y);
                LineTo(e.End.X, e.End.Y);
                LineTo(e.Start.X, e.Start.Y);
                ClosePath();

                //Don't know if last_color has been calculated, so calcking just
                //to be sure.
                last_color = cs.MakeColor(funcs.GetColor(CalcT(tmax, t0, t1)));
                SetColor(last_color);
                Fill();
            }

            #endregion
        }

        public void RenderShade(PdfRadialShading shade, xRectF bounds)
        {
            #region Step 0 Prep work

            SetFillRule(xFillRule.EvenOdd);

            #endregion

            #region Step A - Collect data

            float xa, ya, ra;

            //The coords decides the starting point, the end point and start/end radious.
            var coords = shade.Coords;
            float x0 = (float)coords[0];
            float y0 = (float)coords[1];
            float r0 = (float)coords[2]; //Non negative
            float x1 = (float)coords[3];
            float y1 = (float)coords[4];
            float r1 = (float)coords[5]; //Non negative
            Debug.Assert(r0 >= 0);
            Debug.Assert(r1 >= 0);

            //The bounding box extent. Used for extending the colors of the circle to the
            //boundaries.
            float xmax = bounds.Right;
            float xmin = bounds.Left;
            if (xmax < xmin) { float tmp = xmax; xmax = xmin; xmin = tmp; }
            float ymax = bounds.Top;
            float ymin = bounds.Bottom;
            if (ymax < ymin) { float tmp = ymax; ymax = ymin; ymin = tmp; }

            //Compues the distance between the coordinate points
            float dx = x1 - x0;
            float dy = y1 - y0;
            float dr = r1 - r0;

            //I want to know the maximum and minimum radiouses
            float r_max, r_min;
            if (r1 > r0) { r_max = r1; r_min = r0; }
            else { r_max = r0; r_min = r1; }

            //The t values will be interpolated into this domain
            var domain = shade.Domain;
            float t0 = (float)domain.Min;
            float t1 = (float)domain.Max;
            float dt = t1 - t0;
            float tmin = t0;
            float tmax = t1;

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
                        SetColor(the_col);
                        DrawCircle(x1, y1, r1);
                    }
                    else if (!exts[0] && exts[1])
                    {
                        var the_col = shade.ColorSpace.Converter.MakeColor(funcs.GetColor(t1));
                        SetColor(the_col);

                        RectangleAt(bounds);

                        //Does not matter which circle we add (big or small) as any error
                        //will be overdrawn.
                        DrawCircle(x1, y1, r1);
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
                    float Smin = -r_min / dr;

                    //Then using SMin to find either tmax or tmin
                    float t_tmp = Smin * dt + t0;
                    if (t_tmp > 0) tmax += t_tmp;
                    else tmin = t_tmp;

                    //Smax is trickier, as it's defined as "when the BBox is entierly covered."

                    //This is not entierly correct. HackPaintsEnclosed_diffsize.pdf is an example
                    //of when the ext bounds don't cover the entire bounding box.
                    // ToDo: Fix this
                    if ((start_big) ? exts[0] : exts[1])
                    {
                        var max_col = col_conv.MakeColor(funcs.GetColor(CalcT((start_big) ? tmin : tmax, t0, t1)));
                        SetColor(max_col);

                        RectangleAt(bounds);

                        xa = x0 + t0 / dt * dx;
                        ya = y0 + t0 / dt * dy;
                        ra = r0 + t0 / dt * dr;

                        //Does not matter which circle we add (big or small) as any error
                        //will be overdrawn.
                        DrawCircle(xa, ya, ra);
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
                        SetColor(brush);
                        //double t = s1 * dt + t0;
                        //t = CalcT(t, t0, t1);

                        float s1, s2;
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
                            float xs1 = x0 + s1 * dx;
                            float ys1 = y0 + s1 * dy;
                            var rect = new xRectF(x0, y0 - r0, xs1, ys1 + r0);

                            //Draws
                            DrawRectangle(rect);
                            DrawCircle(x0, y0, r0);
                        }
                        else if (dx == 0) //Straight line along the y axis
                        {
                            s1 = (bounds.Bottom - y0) / dy;
                            s2 = (bounds.Top - y0) / dy;
                            if (s2 < s1) { s1 = s2; }

                            //Calcs the point.
                            float xs1 = x0 + s1 * dx;
                            float ys1 = y0 + s1 * dy;
                            var rect = new xRectF(x0 - r0, y0, xs1 + r0, ys1);

                            //Draws
                            DrawRectangle(rect);
                            DrawCircle(x0, y0, r0);
                        }
                        else
                        {
                            //Formula for a line y = mx + b
                            //Slope of the s axis
                            float slope = dy / dx;

                            //Solve b = y - m*x
                            float b = y0 - slope * x0;

                            //Calc the intersection points for top/bottom.
                            // y = slope * x + b => x = (y - y0) / slope
                            float x_top = (ymax - b) / slope;
                            float x_bottom = (ymin - b) / slope;

                            //Calc the intersection for left/right
                            // y = slope * x + b => 
                            float y_right = slope * xmax + b;
                            float y_left = slope * xmin + b;
                            if (x_top < x_bottom) { b = x_top; x_top = x_bottom; x_bottom = b; }
                            if (y_right < y_left) { b = y_left; y_left = y_right; y_right = b; }

                            //Want the most extreme point values. Technically these are "out of bounds", 
                            //but clipping handles the overdraw.

                            //Two of the values will always be "out of bounds," those are the ones
                            //we want
                            xPointF P_low = new xPointF(xmin, y_left);
                            xPointF P_high = new xPointF(xmax, y_right);
                            if (x_bottom < xmin) P_low = new xPointF(x_bottom, ymin);
                            if (x_top > xmax) P_high = new xPointF(x_top, ymax);

                            //Calculate the s values to figure out what point to use
                            //xc(s) = x0 + s * dx => s = (xc(s) - x0) / dx
                            float s_low = (P_low.X - x0) / dx;
                            float s_high = (P_high.X - x0) / dx;

                            //Mid point
                            xPointF P_mid = new xPointF(x0, y0);

                            //Draws the line
                            if (s_low < s_high)
                                DrawLine(P_low, P_mid, r0 * 2);
                            else
                                DrawLine(P_high, P_mid, r0 * 2);

                            //Draws the circle
                            DrawCircle(P_mid.X, P_mid.Y, r0);
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
                    float Smin = -r_min / dr;
                    float t_tmp = Smin * dt + t0;
                    if (t_tmp > 0) tmax += t_tmp;
                    else tmin = t_tmp;

                    if (r1 < r0 && exts[0])
                    {
                        var brush = col_conv.MakeColor(funcs.GetColor(t0));
                        SetColor(brush);

                        //Quick and dirty. Simply draw ten circles and
                        //call it a day.
                        float t = -1;
                        xa = x0 + t0 / dt * dx; //t0 can be zero, so using
                        ya = y0 + t0 / dt * dy; //-1 instead. (but must draw
                        ra = r0 + t0 / dt * dr; //the first circle)
                        DrawCircle(xa, ya, ra);
                        for (int c = 0; c < 10; c++)
                        {
                            xa = x0 + t / dt * dx;
                            ya = y0 + t / dt * dy;
                            ra = r0 + t / dt * dr;
                            DrawCircle(xa, ya, ra);
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
            float step = (tmax - tmin) / nSamples;

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

                SetColor(col_a);

                //Adds the first circle
                CircleAt(xa, ya, ra);

                //Computes the next circle (B)
                float t = tmin + step * c;

                xa = x0 + (t - t0) / dt * dx;
                ya = y0 + (t - t0) / dt * dy;
                ra = r0 + (t - t0) / dt * dr;

                //Adds the second circle and paints. Note that the paint will
                //be in the form of the area between the edge of the small circle
                //and the edge of the big circle. I.e. one draw a ring of the
                //set color.
                DrawCircle(xa, ya, ra);

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
                    SetColor(brush);

                    if (r0 == r1)
                    {//The circles are not growing or shrinking.

                        if (dy == 0)
                        {
                            float s1 = (bounds.Left - x0) / dx;
                            float s2 = (bounds.Right - x0) / dx;
                            if (s2 < s1) { s2 = s1; }

                            //Calcs the point.
                            float xs2 = x0 + s2 * dx;
                            float ys2 = y0 + s2 * dy;
                            var rect = new xRectF(x1, y1 - r0, xs2, ys2 + r0);

                            // Draws
                            RectangleAt(rect);
                            DrawCircle(x1, y1, r0);
                        }
                        else if (dx == 0)
                        {
                            float s1 = (bounds.Bottom - y0) / dy;
                            float s2 = (bounds.Top - y0) / dy;
                            if (s2 < s1) { s2 = s1; }

                            //Calcs the point.
                            float xs2 = x0 + s2 * dx;
                            float ys2 = y0 + s2 * dy;
                            var rect = new xRectF(x1 - r0, y1, xs2 + r0, ys2);

                            // Draws
                            RectangleAt(rect);
                            DrawCircle(x1, y1, r0);
                        }
                        else
                        {
                            //Formula for a line y = mx + b
                            //Slope of the s axis
                            float slope = dy / dx;

                            //Solve b = y - m*x
                            float b = y0 - slope * x0;

                            //Calc the intersection points for top/bottom.
                            // y = slope * x + b => x = (y - y0) / slope
                            float x_top = (ymax - b) / slope;
                            float x_bottom = (ymin - b) / slope;

                            //Calc the intersection for left/right
                            // y = slope * x + b => 
                            float y_right = slope * xmax + b;
                            float y_left = slope * xmin + b;
                            if (x_top < x_bottom) { b = x_top; x_top = x_bottom; x_bottom = b; }
                            if (y_right < y_left) { b = y_left; y_left = y_right; y_right = b; }

                            //Want the most extreme point values. Technically these are "out of bounds", 
                            //but clipping handles the overdraw.

                            //Two of the values will always be "out of bounds," those are the ones
                            //we want
                            xPointF P_low = new xPointF(xmin, y_left);
                            xPointF P_high = new xPointF(xmax, y_right);
                            if (x_bottom < xmin) P_low = new xPointF(x_bottom, ymin);
                            if (x_top > xmax) P_high = new xPointF(x_top, ymax);

                            //Calculate the s values to figure out what point to use
                            //xc(s) = x0 + s * dx => s = (xc(s) - x0) / dx
                            float s_low = (P_low.X - x0) / dx;
                            float s_high = (P_high.X - x0) / dx;

                            //Mid point
                            xPointF P_mid = new xPointF(x1, y1);

                            //Draws the line
                            if (s_low > s_high)
                                DrawLine(P_low, P_mid, r0 * 2);
                            else
                                DrawLine(P_high, P_mid, r0 * 2);

                            //Draws the circle
                            DrawCircle(P_mid.X, P_mid.Y, r0);
                        }

                    }
                    else
                    {
                        if (r1 > r0)
                        {
                            //Quick and dirty. Simply draw ten circles and
                            //call it a day.
                            float t = t1;
                            for (int c = 0; c < 10; c++)
                            {
                                xa = x0 + t / dt * dx;
                                ya = y0 + t / dt * dy;
                                ra = r0 + t / dt * dr;

                                DrawCircle(xa, ya, ra);
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


            #endregion
        }

        public void RenderShade(PdfFunctionShading shade, xRectF bounds)
        {
            #region Step A - Prepwork

            //Clips, alternativly (and preferably) reduce the Domain to fit inside the
            //bounds when drawing.
            RectangleAt(bounds);
            Clip();

            //Function shaders have their own coordinate system. Sets it up.
            Transform(shade.Matrix);

            //Moves the bounds into the shader's coordinate system.
            //m.Invert();
            //bounds.Transform(m);
            //^This can be used to improve preformance (by figuring out
            // what's not going to be drawn). But isn't needed.

            //Variables needed by the algo
            const int nSamples = 256;
            var domain = shade.Domain;
            float xmin = (float)domain[0].Min;
            float xmax = (float)domain[0].Max;
            float ymin = (float)domain[1].Min;
            float ymax = (float)domain[1].Max;
            float x_step = (xmax - xmin) / nSamples;
            float y_step = (ymax - ymin) / nSamples;
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
            for (float ypos = ymin; ypos < ymax; ypos += y_step)
            {
                for (float xpos = xmin; xpos < xmax; xpos += x_step)
                {
                    //Calculates the color.
                    var col = col_conv.MakeColor(funcs.GetColor(xpos, ypos));

                    //Draws the point.
                    SetColor(col);
                    DrawRectangle(new xRectF(xpos, ypos, xpos + x_step, ypos + y_step));
                }
            }
        }

        public void RenderShade(PdfGouraudShading shade, xRectF bounds)
        {
            #region Step A - Prepwork

            RectangleAt(bounds);
            Clip();

            //Prepeares the shader for use
            var func_ar = shade.Function;
            var td = new TriangleDrawer(this, shade.ColorSpace, func_ar, 50);
            var triangles = shade.Triangles;

            #endregion

            foreach (var triangle in triangles)
            {
                td.Draw3ColorTriangle(new xPointF(triangle.VA),
                                      new xPointF(triangle.VB),
                                      new xPointF(triangle.VC),
                                      triangle.C_VA,
                                      triangle.C_VB,
                                      triangle.C_VC);
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
        public void RenderShade(PdfPatchShading shade, xRectF bounds)
        {
            #region Step A - Prepwork

            RectangleAt(bounds);
            Clip();

            //Prepeares the shader for use
            var func_ar = shade.Function;
            var td = new TriangleDrawer(this, shade.ColorSpace, func_ar);
            var tensors = shade.TensorsF;

            #endregion

            foreach (var tensor in tensors)
            {
                const int NUMBER_OF_ITTERATIONS = 3; //3 = 4 times 4 times 4 splits (64 total)
                DrawTensor(td, tensor, NUMBER_OF_ITTERATIONS, NUMBER_OF_ITTERATIONS);

                //DrawTensor(dc, td, tensor, 0);
            }
        }

        protected abstract void SetFillRule(xFillRule fr);
        protected abstract void MoveTo(float x, float y);
        protected abstract void LineTo(float x, float y);
        protected abstract void ClosePath();
        public abstract void SetColor(DblColor col);
        protected abstract void RectangleAt(xRectF rect);
        protected abstract void CircleAt(float x, float y, float radius);
        protected abstract void DrawLine(xPointF start, xPointF end, float width);
        protected abstract void DrawCircle(float x, float y, float radius);
        public abstract void DrawRectangle(xRectF rect);
        protected abstract void DrawRectangle(DblColor col, xRectF rect);
        protected abstract void Transform(xMatrix transform);
        protected abstract void Fill();
        protected abstract void Clip();

        static float CalcT(float t, float t0, float t1)
        {
            if (t < 0) return t0;
            else if (t > 1) return t1;
            else return t0 + (t1 - t0) * t;
        }

        private class TriangleDrawer
        {
            private readonly ColorStepper _stepper;
            private ColorConverter _cc;
            SingleInputFunctions _func;
            private readonly int _n_passes;
            private readonly rPattern _dc;

            public TriangleDrawer(rPattern dc, IColorSpace cs, PdfFunctionArray funcs, int n_passes = 5)
            {
                _dc = dc;
                _n_passes = n_passes;
                _stepper = new ColorStepper(funcs != null ? 1 : cs.NComponents);
                _cc = cs.Converter;
                if (funcs != null)
                    _func = new SingleInputFunctions(funcs);
            }

            /// <summary>
            /// 
            ///       P2
            ///      /  \
            ///   A /    \ C
            ///    /      \
            ///   /   B    \
            ///  P1---------P3
            /// </summary>
            internal void Draw3ColorTriangle(xPointF p1, xPointF p2, xPointF p3,
                double[] p1_color, double[] p2_color, double[] p3_color)
            {
                _stepper.Init(p1_color, p2_color, p3_color, _n_passes - 1);

                //Debug lines
                //_dc.SetColor(Compose.cColor.GRAY.ToRGBColor().ToDblColor());
                //_dc.DrawLine(p1, p2, 1f);
                //_dc.DrawLine(p2, p3, 1f);
                //_dc.DrawLine(p1, p3, 1f);

                //Used for calculating new points on the lines
                float a_dx = p2.X - p1.X, a_dy = p2.Y - p1.Y;// a_slope = a_dy / a_dx;
                float b_dx = p3.X - p1.X, b_dy = p3.Y - p1.Y;// b_slope = b_dy / b_dx;
                float c_dx = p2.X - p3.X, c_dy = p2.Y - p3.Y;

                float t_step = 1f / _n_passes;

                //t = 0 is the start of the line. T = 1 is the end
                float A_t = t_step; //t along the A line

                xPointF bottom_p1 = p1, bottom_p2 = p3;

                //We draw from B to P2
                for (int c = 1, stop = _n_passes; c < _n_passes; c++, A_t += t_step)
                {
                    xPointF top_p1, top_p2;

                    //First we fine a line a short distance up
                    top_p1.X = a_dx * A_t + p1.X;
                    top_p1.Y = a_dy * A_t + p1.Y;
                    top_p2.X = c_dx * A_t + p3.X;
                    top_p2.Y = c_dy * A_t + p3.Y;


                    float B_t = t_step; //t along the B line
                    xPointF last_bottom_point = bottom_p1, last_top_point = top_p1;
                    for (int k = 1; k < stop; k++, B_t += t_step)
                    {
                        xPointF right_pt;

                        //Next we find a point on the bottom line.
                        right_pt.X = b_dx * B_t + bottom_p1.X;
                        right_pt.Y = b_dy * B_t + bottom_p1.Y;

                        //Then we find the point where a line drawn from X, Y, parralel to Line A,
                        //interesects with the line "top", which is parralell with Line B
                        xPointF xp;
                        xp.X = b_dx * B_t + top_p1.X;
                        xp.Y = b_dy * B_t + top_p1.Y;

                        DrawTriangle(last_bottom_point, last_top_point, right_pt, _stepper.Current);
                        DrawTriangle(last_top_point, xp, right_pt, _stepper.TopCurrent);

                        last_bottom_point = right_pt;
                        last_top_point = xp;
                        _stepper.StepRight();
                    }

                    //Draws the rightmost triangle
                    DrawTriangle(last_bottom_point, last_top_point, bottom_p2, _stepper.BottomRight);

                    bottom_p1 = top_p1;
                    bottom_p2 = top_p2;

                    //-1 because we draw the final triangle outside the loop
                    _stepper.StepUp(--stop - 1);
                }

                //Draws the topmost triangle
                DrawTriangle(bottom_p1, p2, bottom_p2, p2_color);
            }

            /// <summary>
            /// 
            ///       P2
            ///      /  \
            ///     /    \ 
            ///    /      \
            ///   /        \
            ///  P1---------P3
            /// </summary>
            private void DrawTriangle(xPointF p1, xPointF p2, xPointF p3, double[] col)
            {
                _dc.MoveTo(p1.X, p1.Y);
                _dc.LineTo(p2.X, p2.Y);
                _dc.LineTo(p3.X, p3.Y);
                _dc.ClosePath();
                
                DblColor dbl = _cc.MakeColor(_func == null ? col : _func.GetColor(col[0]));

                _dc.SetColor(dbl);
                _dc.Fill();
            }
        }

        /// <summary>
        ///       P2        
        ///      /  \
        ///   A /    \ C
        ///    /      \
        ///   /   B    \
        ///  P1---------P3
        ///  
        ///  P2 = _end_color
        ///  P1 = _bottom_left_color
        ///  P3 = _bottom_right_color
        /// </summary>
        private class ColorStepper
        {
            /// <summary>
            /// Color that left and right color converges towards
            /// </summary>
            double[] _end_color;

            /// <summary>
            /// Colors on the "next" step towards the top
            /// </summary>
            double[] _top_left_color, _top_right_color;

            /// <summary>
            /// Colors that are stepped towards the top
            /// </summary>
            double[] _bottom_left_color, _bottom_right_color;

            /// <summary>
            /// How much to move for each step towards the top
            /// </summary>
            double[] _step_to_top_left, _step_to_top_right;

            /// <summary>
            /// How much to move for each step towards the left
            /// </summary>
            double[] _step_to_right, _top_step_to_right;

            /// <summary>
            /// Current colors
            /// </summary>
            double[] _current_left, _top_current_left;


            public double[] Current => _current_left;
            public double[] TopCurrent => _top_current_left;

            public double[] BottomRight => _bottom_right_color;

            public ColorStepper(int n_channels)
            {
                _top_left_color = new double[n_channels];
                _top_right_color = new double[n_channels];
                _bottom_left_color = new double[n_channels];
                _bottom_right_color = new double[n_channels];
                _step_to_top_left = new double[n_channels];
                _step_to_top_right = new double[n_channels];
                _current_left = new double[n_channels];
                _top_current_left = new double[n_channels];
                _step_to_right = new double[n_channels];
                _top_step_to_right = new double[n_channels];
            }

            public void Init(double[] left_color, double[] top_color, double[] right_color, int n_steps)
            {
                //This color will not be altered, so we don't copy it
                _end_color = top_color;

                //Copies the colors
                for (int c = 0; c < _bottom_left_color.Length; c++)
                {
                    //       Top
                    //      / 
                    //     /   
                    //    /   
                    //   /   <- _step_to_top_(from)_left
                    // Bottom left
                    //
                    double col = _bottom_left_color[c] = _current_left[c] = left_color[c];
                    _step_to_top_left[c] = (_end_color[c] - col) / n_steps;


                    //   Top
                    //     \
                    //      \  
                    //       \
                    //        \ <- _step_to_top_(from)_right
                    //     Bottom right
                    col = _bottom_right_color[c] = right_color[c];
                    _step_to_top_right[c] = (_end_color[c] - col) / n_steps;




                    //                      Top
                    //                      /  \
                    //                     /    \  
                    // Top left color ->  /------\ <- Top right color
                    //                   /        \ <- _step_to_top
                    //                   Bottom line
                    //
                    _top_left_color[c] = _top_current_left[c] = _bottom_left_color[c] + _step_to_top_left[c];
                    _top_right_color[c] = _bottom_right_color[c] + _step_to_top_right[c];

                    //      /  \
                    //     /    \
                    //    /------\   <-- Top line
                    //   /  ^-----\--- _top_step_to_right
                    //  P1---------P3 <-- Bottom line
                    //     ^-- _step_to_right
                    //Note, the bottom has "one more step" than the top. This is because the line is longer.
                    _step_to_right[c] = (right_color[c] - left_color[c]) / (n_steps);
                    _top_step_to_right[c] = (_top_right_color[c] - _top_left_color[c]) / (n_steps - 1);

                    //_top_current_left[c] += _step_to_top_right[c];
                }


            }

            public void StepUp(int hrz_steps)
            {
                //Swaps top for bottom
                double[] old_top = _top_left_color;
                _top_left_color = _bottom_left_color;
                _bottom_left_color = old_top;

                old_top = _top_right_color;
                _top_right_color = _bottom_right_color;
                _bottom_right_color = old_top;

                old_top = _top_step_to_right;
                _top_step_to_right = _step_to_right;
                _step_to_right = old_top;

                for (int c = 0; c < _top_left_color.Length; c++)
                {
                    //Calculate new top colors
                    _top_left_color[c] = _bottom_left_color[c] + _step_to_top_left[c];
                    _top_right_color[c] = _bottom_right_color[c] + _step_to_top_right[c];

                    //How much for each horizontal step
                    _top_step_to_right[c] = (_top_right_color[c] - _top_left_color[c]) / hrz_steps;
                    _step_to_right[c] = (_bottom_right_color[c] - _bottom_left_color[c]) / (hrz_steps - 1);

                    //Sets up the current colors
                    _current_left[c] = _bottom_left_color[c];
                    _top_current_left[c] = _top_left_color[c];
                }
            }

            public void StepRight()
            {
                for (int c = 0; c < _current_left.Length; c++)
                {
                    _current_left[c] += _step_to_right[c];
                    _top_current_left[c] += _top_step_to_right[c];
                }
            }
        }

        private static void DrawTensor(TriangleDrawer td, PdfTensorF t, int vert_splits, int hrz_splits)
        {
            PdfTensorF first, last;

            SplitTensorHorizontal(t, out first, out last);

            if (hrz_splits == 0)
            {
                //DrawTensor(dc, td, first);
                //DrawTensor(dc, td, last);
                DrawTensor(td, first, vert_splits);
                DrawTensor(td, last, vert_splits);
            }
            else
            {
                DrawTensor(td, first, vert_splits, hrz_splits - 1);
                DrawTensor(td, last, vert_splits, hrz_splits - 1);
            }
        }

        private static void DrawTensor(TriangleDrawer td, PdfTensorF t, int itteration)
        {
            PdfTensorF first, last;

            SplitTensorVertical(t, out first, out last);

            if (itteration == 0)
            {
                DrawTensor(td, first);
                DrawTensor(td, last);
            }
            else
            {
                DrawTensor(td, first, itteration - 1);
                DrawTensor(td, last, itteration - 1);
            }
        }

        private static void DrawTensor(TriangleDrawer td, PdfTensorF t)
        {
            td.Draw3ColorTriangle(t.P00, t.P03, t.P30,
                    t.Color_ll, t.Color_ul, t.Color_lr);
            td.Draw3ColorTriangle(t.P03, t.P33, t.P30,
                    t.Color_ul, t.Color_ur, t.Color_lr);
        }

        private static void SplitTensorVertical(PdfTensorF tense, out PdfTensorF first, out PdfTensorF last)
        {
            xPointF[,] first_coords = new xPointF[4, 4], last_coords = new xPointF[4, 4];
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

            first = new PdfTensorF(first_coords, tense.Color_ll, tense.Color_ul, top, bottom);
            last = new PdfTensorF(last_coords, bottom, top, tense.Color_ur, tense.Color_lr);
        }

        private static void SplitTensorHorizontal(PdfTensorF tense, out PdfTensorF first, out PdfTensorF last)
        {
            xPointF[,] first_coords = new xPointF[4, 4], last_coords = new xPointF[4, 4];
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

            first = new PdfTensorF(first_coords, tense.Color_ll, left, right, tense.Color_lr);
            last = new PdfTensorF(last_coords, left, tense.Color_ul, tense.Color_ur, right);
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
        private static void SplitBezier(xPointF[,] line, byte x, byte y, byte adv_x, byte adv_y, xPointF[,] first, xPointF[,] last)
        {
            //With midpoint at 0.5, the math can be simplified over the current implementation
            // (x2 - x1) * 0.5 + x1 == (x2 + x1) * 0.5
            const float t = 0.5f;

            xPointF start = line[x, y];
            xPointF ctrl1 = line[x + adv_x, y + adv_y];
            xPointF ctrl2 = line[x + adv_x * 2, y + adv_y * 2];
            xPointF end = line[x + adv_x * 3, y + adv_y * 3];

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
            last[x, y] = new xPointF(x1234, y1234);
            first[x + adv_x, y + adv_y] = new xPointF(x12, y12);
            last[x + adv_x, y + adv_y] = new xPointF(x234, y234);
            first[x + adv_x * 2, y + adv_y * 2] = new xPointF(x123, y123);
            last[x + adv_x * 2, y + adv_y * 2] = new xPointF(x34, y34);
            first[x + adv_x * 3, y + adv_y * 3] = new xPointF(x1234, y1234);
            last[x + adv_x * 3, y + adv_y * 3] = end;

            //return new xPoint[] { new xPoint(x12, y12), new xPoint(x123, y123), new xPoint(x1234, y1234) };

            //[(x1,y1),(x12,y12),(x123,y123),(x1234,y1234),(x234,y234),(x34,y34),(x4,y4)] 
        }
    }
}
