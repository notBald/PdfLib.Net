using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.Function;
using PdfLib.Pdf.Primitives;
using PdfLib.Util;
using System.Windows.Media;

namespace PdfLib.Render.WPF
{
    internal class TriangleDrawer
    {
        private readonly ColorStepper _stepper;
        //private IColorSpace _cs;
        private Pdf.ColorSpace.ColorConverter _cc;
        SingleInputFunctions _func;
        private readonly int _n_passes;

        public TriangleDrawer(IColorSpace cs, PdfFunctionArray funcs, int n_passes = 5)
        {
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
        internal void Draw3ColorTriangle(DrawingContext dc, xPoint p1, xPoint p2, xPoint p3,
            double[] p1_color, double[] p2_color, double[] p3_color)
        {
            _stepper.Init(p1_color, p2_color, p3_color, _n_passes - 1);

            //var p_brown = new Pen(Brushes.Brown, 1);
            //dc.DrawLine(p_brown, Util.XtoWPF.ToPoint(p1), Util.XtoWPF.ToPoint(p2));
            //dc.DrawLine(p_brown, Util.XtoWPF.ToPoint(p2), Util.XtoWPF.ToPoint(p3));
            //dc.DrawLine(p_brown, Util.XtoWPF.ToPoint(p1), Util.XtoWPF.ToPoint(p3));

            //Used for calculating new points on the lines
            double a_dx = p2.X - p1.X, a_dy = p2.Y - p1.Y;// a_slope = a_dy / a_dx;
            double b_dx = p3.X - p1.X, b_dy = p3.Y - p1.Y;// b_slope = b_dy / b_dx;
            double c_dx = p2.X - p3.X, c_dy = p2.Y - p3.Y;

            //We do 10 samples for now
            double t_step = 1d / _n_passes;

            //t = 0 is the start of the line. T = 1 is the end
            double A_t = t_step; //t along the A line

            xPoint bottom_p1 = p1, bottom_p2 = p3;

            //We draw from B to P2
            for (int c = 1, stop = _n_passes; c < _n_passes; c++, A_t += t_step)
            {
                xPoint top_p1, top_p2;

                //First we fine a line a short distance up
                top_p1.X = a_dx * A_t + p1.X;
                top_p1.Y = a_dy * A_t + p1.Y;
                top_p2.X = c_dx * A_t + p3.X;
                top_p2.Y = c_dy * A_t + p3.Y;
                //double top_y_intercept = top_p1.Y - b_slope * top_p1.X;

                //dc.DrawLine(p_brown, Util.XtoWPF.ToPoint(top_p1), Util.XtoWPF.ToPoint(top_p2));


                double B_t = t_step; //t along the B line
                xPoint last_bottom_point = bottom_p1, last_top_point = top_p1;
                for (int k = 1; k < stop; k++, B_t += t_step)
                {
                    xPoint right_pt;

                    //Next we find a point on the bottom line.
                    right_pt.X = b_dx * B_t + bottom_p1.X;
                    right_pt.Y = b_dy * B_t + bottom_p1.Y;
                    //dc.DrawEllipse(Brushes.Gray, null, XtoWPF.ToPoint(right_pt), 5, 5);
                    //double right_y_intercept = right_pt.Y - a_slope * right_pt.X;

                    //Then we find the point where a line drawn from X, Y, parralel to Line A,
                    //interesects with the line "top", which is parralell with Line B
                    //xPoint xp = CalcIntersection(b_slope, top_y_intercept, a_slope, right_y_intercept);
                    xPoint xp;
                    xp.X = b_dx * B_t + top_p1.X;
                    xp.Y = b_dy * B_t + top_p1.Y;

                    //dc.DrawEllipse(Brushes.Black, null, XtoWPF.ToPoint(xp), 5, 5);

                    DrawTriangle(dc, last_bottom_point, last_top_point, right_pt, _stepper.Current);
                    DrawTriangle(dc, last_top_point, xp, right_pt, _stepper.TopCurrent);

                    last_bottom_point = right_pt;
                    last_top_point = xp;
                    _stepper.StepRight();
                }

                //Draws the rightmost triangle
                DrawTriangle(dc, last_bottom_point, last_top_point, bottom_p2, _stepper.BottomRight);

                bottom_p1 = top_p1;
                bottom_p2 = top_p2;

                //-1 because we draw the final triangle outside the loop
                _stepper.StepUp(--stop - 1);
            }

            //Draws the topmost triangle
            DrawTriangle(dc, bottom_p1, p2, bottom_p2, p2_color);
        }

        internal static void DrawTensor(DrawingContext dc, Pdf.ColorSpace.Pattern.PdfTensor t, Pen p)
        {
            DrawBezier(p, dc, t.P00, t.P01, t.P02, t.P03);
            DrawBezier(p, dc, t.P03, t.P13, t.P23, t.P33);
            DrawBezier(p, dc, t.P33, t.P32, t.P31, t.P30);
            DrawBezier(p, dc, t.P00, t.P10, t.P20, t.P30);
        }

        private static void DrawBezier(Pen p, DrawingContext dc, xPoint start, xPoint cp1, xPoint cp2, xPoint end)
        {
            StreamGeometry sg = new StreamGeometry();
            using (var sgc = sg.Open())
            {
                sgc.BeginFigure(Util.XtoWPF.ToPoint(start), false, false);
                sgc.BezierTo(Util.XtoWPF.ToPoint(cp1), Util.XtoWPF.ToPoint(cp2), Util.XtoWPF.ToPoint(end), true, false);
            }
            dc.DrawGeometry(null, p, sg);
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
        private void DrawTriangle(DrawingContext dc, xPoint p1, xPoint p2, xPoint p3, double[] col)
        {
            var sg = new StreamGeometry();
            using (var sc = sg.Open())
            {
                sc.BeginFigure(XtoWPF.ToPoint(p1), true, true);
                sc.LineTo(XtoWPF.ToPoint(p2), false, false);
                sc.LineTo(XtoWPF.ToPoint(p3), false, false);
            }
            sg.Freeze();

            DblColor dbl = _cc.MakeColor(_func == null ? col : _func.GetColor(col[0]));

            dc.DrawGeometry(new SolidColorBrush(ToWPF(dbl)), /*new Pen(Brushes.Brown, 1)*/ null, sg);
        }

        static Color ToWPF(PdfColor c)
        {
            var col = c.ToARGB();
            return Color.FromArgb(col[0], col[1], col[2], col[3]);
        }

        /// <summary>
        /// Find the X, Y value where two infinite lines intersect
        /// </summary>
        /// <remarks>
        /// Line formula line 1: Y1 = l1_slope * X1 + l1_offset
        /// Line formula line 2: Y2 = l2_slope * X2 + l2_offset
        ///
        /// First we solve for X
        /// 
        ///     We know that Y1 == Y2 and X1 == X2 so
        ///
        ///         l1_slope * X + l1_offset == l2_slope * X + l2_offset
        ///
        ///     We move X to the right:
        ///
        ///         l1_slope * X - l2_slope * X == l2_offset - l1_offset
        ///
        ///     We extract X:
        ///
        ///         X / (l1_slope - l2_slope) = l2_offset - l1_offset
        ///
        ///     We devide with (l1_slope - l2_slope) on both sides
        ///
        ///         x = (l2_offset - l1_offset) / (l1_slope - l2_slope)
        ///         
        /// Now we solve for Y
        /// 
        ///     We know the line formula is Y = l1_slope * x + l1_offset, and that
        ///     X = (l2_offset - l1_offset) / (l1_slope - l2_slope)
        ///     
        ///     So we insert X into that formula:
        ///         Y = l1_slope * (l2_offset - l1_offset) / (l1_slope - l2_slope) + l1_offset
        ///         
        /// And we're done. Been a while.
        /// </remarks>
        private static xPoint CalcIntersection(double l1_slope, double l1_offset_y, double l2_slope, double l2_offset_y)
        {
            double x = (l2_offset_y - l1_offset_y) / (l1_slope - l2_slope);

            return new xPoint(x, l1_slope * x + l1_offset_y);
        }

        ///// <summary>
        ///// Slope formula: 
        /////      x1 - x2 (dx)
        /////  m = -------
        /////      y1 - y2 (dy)
        ///// </summary>
        //private static double CalcSlope(xPoint start, xPoint end)
        //{
        //    return (end.X - start.X) / (end.Y - start.Y);
        //}


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
    }
}
