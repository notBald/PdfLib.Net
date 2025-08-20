using System;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Pdf.Function;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;

namespace PdfLib.Compose
{
    /// <summary>
    /// An axial shader that traverses from start point to end point
    /// </summary>
    public class cLinearGradientBrush : cBrush
    {
        /// <summary>
        /// Convinience property for retriving the shader
        /// </summary>
        PdfAxialShading Shader { get { return (PdfAxialShading)((PdfShadingPattern)_pattern).Shading; } }

        /// <summary>
        /// Defines the coorinate system for this brush
        /// </summary>
        public xMatrix Matrix 
        { 
            get { return ((PdfShadingPattern)_pattern).Matrix; }
            set { ((PdfShadingPattern)_pattern).Matrix = value; }
        }

        /// <summary>
        /// The shader's color space
        /// </summary>
        public override IColorSpace ColorSpace { get { return Shader.ColorSpace; } }

        /// <summary>
        /// Colors in this gradient brush
        /// </summary>
        public ColorRange[] Colors
        {
            get
            {
                var func = Shader.Function[0];
                var cs = ColorSpace;
                if (func is PdfExponentialFunction)
                {
                    var ex = (PdfExponentialFunction)func;
                    return new ColorRange[] {
                        new ColorRange(
                            new ColorStop(0, new cColor(ex.C0, cs)),
                            new ColorStop(1, new cColor(ex.C1, cs))
                    )};
                }
                var sf = (PdfStitchingFunction)func;
                var bounds = sf.Bounds;
                var funcs = sf.Functions;
                var ret = new ColorRange[funcs.Length];
                double bound = 0;
                for(int c=0; c < funcs.Length; c++)
                {
                    var ex = (PdfExponentialFunction)funcs[c];
                    var from = new ColorStop(bound, new cColor(ex.C0, cs));
                    bound = c == bounds.Length ? 1 : bounds[c];
                    var to = new ColorStop(bound, new cColor(ex.C1, cs));
                    ret[c] = new ColorRange(from, to);
                }

                return ret;
            }
            set
            {
                if (value == null || value.Length == 0)
                    throw new PdfNotSupportedException("Array must have a length of 1 or more");
                var cs = ColorSpace;
                var fa = new PdfItem[value.Length];
                var bounds = new double[fa.Length - 1];
                var encode = new xRange[fa.Length];
                for (int c = 0; c < value.Length; c++)
                {
                    var r = value[c];
                    var from = ((cColor)r.From.Color.ConvertTo(cs)).ToArray();
                    var to = ((cColor)r.To.Color.ConvertTo(cs)).ToArray();
                    fa[c] = new PdfExponentialFunction(1, from, to, null, null);

                    var b = c - 1;
                    if (b >= 0)
                        bounds[b] = r.From.Start;
                    encode[c] = new xRange(0, 1);
                }
                if (value.Length == 1)
                    Shader.Function[0] = (PdfFunction) fa[0];
                else
                {
                    
                    var pfa = new PdfFunctionArray(fa, false, false);
                    var sf = new PdfStitchingFunction(pfa, bounds, encode);
                    Shader.Function[0] = sf;
                }
            }
        }

        /// <summary>
        /// If color is to be extended after the end point
        /// </summary>
        public bool ExtendAfter
        {
            get { return Shader.Extend.ExtendAfter; }
            set { Shader.Extend = new ExtendShade(ExtendBefore, value); }
        }

        /// <summary>
        /// If color is to be extended before the start point
        /// </summary>
        public bool ExtendBefore
        {
            get { return Shader.Extend.ExtendBefore; }
            set { Shader.Extend = new ExtendShade(value, ExtendAfter); }
        }

        /// <summary>
        /// Color on area that is not filled. 
        /// </summary>
        public cColor Background
        {
            get 
            { 
                var shade = Shader;
                return new cColor(shade.Background, shade.ColorSpace);
            }
            set
            {
                if (value == null)
                {
                    Shader.Background = null;
                    return;
                }
                var shade = Shader;
                shade.BackgroundAr = ((cColor) value.ConvertTo(shade.ColorSpace)).ToArray();
            }
        }

        /// <summary>
        /// Creates a gradient brush
        /// </summary>
        /// <param name="start">Starting point</param>
        /// <param name="stop">Ending point</param>
        /// <param name="start_color">Start point color, will also determine the shader's color space</param>
        /// <param name="end_color">End point color</param>
        public cLinearGradientBrush(xPoint start, xPoint stop, cColor start_color, cColor end_color)
            : base(PatternCS.Instance)
        {
            var cs = start_color.MyColorSpace;
            var func = new PdfExponentialFunction(1, start_color.ToArray(), ((cColor) end_color.ConvertTo(cs)).ToArray(), null, null);
            var pat = new PdfAxialShading(new PdfRectangle(start.X, start.Y, stop.X, stop.Y), cs, func);
            pat.Extend = new ExtendShade(true, true);
            var shader = new PdfShadingPattern(pat);

            _pattern = shader;
        }

        /// <summary>
        /// Creates a gradient brush
        /// </summary>
        /// <param name="start_x">Starting point</param>
        /// <param name="start_y">Starting point</param>
        /// <param name="stop_x">Ending point</param>
        /// <param name="stop_y">Ending point</param>
        /// <param name="start_color">Start point color, will also determine the shader's color space</param>
        /// <param name="end_color">End point color</param>
        public cLinearGradientBrush(double start_x, double start_y, double stop_x, double stop_y, cColor start_color, cColor end_color)
            : this(new xPoint(start_x, start_y), new xPoint(stop_x, stop_y), start_color, end_color)
        { }

        /// <summary>
        /// Adds a color stop at the end of the color list
        /// </summary>
        /// <param name="start">New start position of the last color in the list</param>
        /// <param name="end_color">Color at position 1</param>
        public void AddColorStop(double start, cColor end_color)
        {
            var colors = Colors;
            var l = colors.Length;
            Array.Resize<ColorRange>(ref colors, l + 1);
            //colors[l - 1].To.Start = stop; //Ignored
            colors[l] = new ColorRange(
                new ColorStop(start, colors[l - 1].To.Color),
                new ColorStop(1, end_color));
            Colors = colors;
        }
    }
}