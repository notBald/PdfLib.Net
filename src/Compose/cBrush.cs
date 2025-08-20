using System;
using System.Collections.Generic;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Pdf.ColorSpace.Pattern;
using PdfLib.Compile;
using PdfLib.Render;
using PdfLib.Render.Commands;

namespace PdfLib.Compose
{
    /// <summary>
    /// Compose Color
    /// 
    /// Convenience class that encapsulates every color variation, including color space, that
    /// PDF supports. 
    /// </summary>
    public abstract class cBrush
    {
        #region Variables

        internal readonly IColorSpace MyColorSpace;
        protected double[] _color;
        protected PdfPattern _pattern;

        internal DblColor Color
        {
            get
            {
                if (MyColorSpace == null || _color == null) return null;
                return MyColorSpace.Converter.MakeColor(_color);
            }
        }

        public virtual IColorSpace ColorSpace { get { return MyColorSpace; } }

        #endregion

        #region Init

        protected cBrush(IColorSpace cs) { MyColorSpace = cs; }

        #endregion

        #region Make CMD

        /// <summary>
        /// Makes a render CMD function from this command
        /// </summary>
        /// <param name="cs">A color space or null, 
        /// used to see if there's a need to emit a CS command</param>
        /// <returns>The render CMD function</returns>
        internal void MakeCMD(cColor cs, bool fill, List<RenderCMD> cmds)
        {
            if (MyColorSpace is DeviceRGB)
            {
                if (_color[0] == _color[1] && _color[0] == _color[2])
                    cmds.Add(fill ? (RenderCMD)new g_CMD(_color[0])
                              : new G_CMD(_color[0]));
                else
                    cmds.Add(fill ? (RenderCMD)new rg_CMD(_color[2], _color[1], _color[0])
                                  : new RG_CMD(_color[2], _color[1], _color[0]));
            }
            else if (MyColorSpace is DeviceGray)
                cmds.Add(fill ? (RenderCMD)new g_CMD(_color[0])
                              : new G_CMD(_color[0]));
            else if (MyColorSpace is DeviceCMYK)
                cmds.Add(fill ? (RenderCMD)new k_CMD(_color[3], _color[2], _color[1], _color[0])
                              : new K_CMD(_color[3], _color[2], _color[1], _color[0]));
            else if (MyColorSpace is CSPattern)
                cmds.Add(fill ? (RenderCMD)new scn_tile_CMD(_color, ((CSPattern)MyColorSpace).CPat)
                              : new SCN_tile_CMD(_color, ((CSPattern)MyColorSpace).CPat));
            else
            {
                if (cs == null || !ReferenceEquals(MyColorSpace, cs.MyColorSpace))
                    cmds.Add(fill ? (RenderCMD)new cs_CMD(MyColorSpace)
                                  : new CS_CMD(MyColorSpace));

                if (MyColorSpace is ICCBased)
                {
                    cmds.Add(fill ? (RenderCMD)new scn_CMD(_color)
                                  : new SCN_CMD(_color));
                }
                else if (_color != null)
                {
                    cmds.Add(fill ? (RenderCMD)new sc_CMD(_color)
                                  : new SC_CMD(_color));
                }
                else
                {
                    if (_pattern is PdfTilingPattern)
                        cmds.Add(fill ? (RenderCMD)new scn_raw_tile_CMD(_color, (PdfTilingPattern)_pattern)
                                      : new SCN_raw_tile_CMD(_color, (PdfTilingPattern)_pattern));
                    else
                        cmds.Add(fill ? (RenderCMD)new scn_pattern_CMD((PdfShadingPattern)_pattern)
                                      : new SCN_pattern_CMD((PdfShadingPattern)_pattern));
                }
            }
        }

        /// <summary>
        /// Makes a render CMD function from this command
        /// </summary>
        /// <param name="cs">A color space or null, 
        /// used to see if there's a need to emit a CS command</param>
        /// <returns>The render CMD function</returns>
        internal void MakeCMD(cBrush cs, bool fill, IDraw draw)
        {
            if (MyColorSpace is DeviceRGB)
            {
                if (_color[0] == _color[1] && _color[0] == _color[2])
                {
                    if (fill)
                        draw.SetFillColor(_color[0]);
                    else
                        draw.SetStrokeColor(_color[0]);
                }
                else
                {
                    if (fill)
                        draw.SetFillColor(_color[0], _color[1], _color[2]);
                    else
                        draw.SetStrokeColor(_color[0], _color[1], _color[2]);
                }
            }
            else if (MyColorSpace is DeviceGray)
            {
                if (fill)
                    draw.SetFillColor(_color[0]);
                else
                    draw.SetStrokeColor(_color[0]);
            }
            else if (MyColorSpace is DeviceCMYK)
            {
                if (fill)
                    draw.SetFillColor(_color[0], _color[1], _color[2], _color[3]);
                else
                    draw.SetStrokeColor(_color[0], _color[1], _color[2], _color[3]);
            }
            else if (MyColorSpace is CSPattern)
            {
                if (fill)
                    draw.SetFillPattern(_color, ((CSPattern)MyColorSpace).CPat);
                else
                    draw.SetStrokePattern(_color, ((CSPattern)MyColorSpace).CPat);
            }
            else
            {
                if (cs == null || !ReferenceEquals(MyColorSpace, cs.MyColorSpace))
                {
                    if (fill)
                        draw.SetFillCS(MyColorSpace);
                    else
                        draw.SetStrokeCS(MyColorSpace);
                }

                if (MyColorSpace is ICCBased)
                {
                    if (fill)
                        draw.SetFillColor(_color);
                    else
                        draw.SetStrokeColor(_color);
                }
                else if (_color != null)
                {
                    if (fill)
                        draw.SetFillColorSC(_color);
                    else
                        draw.SetStrokeColorSC(_color);
                }
                else
                {
                    if (_pattern is PdfTilingPattern)
                    {
                        if (fill)
                            draw.SetFillPattern(_color, (PdfTilingPattern)_pattern);
                        else
                            draw.SetStrokePattern(_color, (PdfTilingPattern)_pattern);
                    }
                    else
                    {
                        if (fill)
                            draw.SetFillPattern((PdfShadingPattern)_pattern);
                        else
                            draw.SetStrokePattern((PdfShadingPattern)_pattern);
                    }
                }
            }
        }

        internal void MakeCMD(IDraw draw, bool fill)
        {
            var list = new List<RenderCMD>(3);
            MakeCMD(null, fill, list);
            foreach (var cmd in list)
                cmd.Execute(draw);
        }

        internal void SetColor(cRenderState state, bool fill, IDraw draw)
        {
            if (fill)
            {
                if (Equals(state.fill_color))
                    return;
            }
            else
            {
                if (Equals(state.stroke_color))
                    return;
            }
            if (MyColorSpace is DeviceRGB)
            {
                if (_color[0] == _color[1] && _color[0] == _color[2])
                    if (fill) draw.SetFillColor(_color[0]);
                    else draw.SetStrokeColor(_color[0]);
                else
                    if (fill) draw.SetFillColor(_color[0], _color[1], _color[2]);
                    else draw.SetStrokeColor(_color[0], _color[1], _color[2]);
            }
            else if (MyColorSpace is DeviceGray)
                if (fill) draw.SetFillColor(_color[0]);
                else draw.SetStrokeColor(_color[0]);
            else if (MyColorSpace is DeviceCMYK)
                if (fill) draw.SetFillColor(_color[0], _color[1], _color[2], _color[3]);
                else draw.SetStrokeColor(_color[0], _color[1], _color[2], _color[3]);
            else if (MyColorSpace is CSPattern)
                if (fill) draw.SetFillPattern(_color, ((CSPattern)MyColorSpace).CPat);
                else draw.SetStrokePattern(_color, ((CSPattern)MyColorSpace).CPat);
            else
            {
                if (fill)
                {
                    if (!ReferenceEquals(MyColorSpace, state.fill_color.MyColorSpace))
                        draw.SetFillCS(MyColorSpace);
                }
                else
                {
                    if (!ReferenceEquals(MyColorSpace, state.stroke_color.MyColorSpace))
                        draw.SetStrokeCS(MyColorSpace);
                }

                if (MyColorSpace is ICCBased)
                {
                    if (fill) draw.SetFillColor(_color);
                    else draw.SetStrokeColor(_color);
                }
                else if (_color != null)
                {
                    if (fill) draw.SetFillColorSC(_color);
                    else draw.SetStrokeColorSC(_color);
                }
                else
                {
                    if (_pattern is PdfTilingPattern)
                        if (fill) draw.SetFillPattern(_color, (PdfTilingPattern)_pattern);
                        else draw.SetStrokePattern(_color, (PdfTilingPattern)_pattern);
                    else
                        if (fill) draw.SetFillPattern((PdfShadingPattern)_pattern);
                        else draw.SetStrokePattern((PdfShadingPattern)_pattern);
                }
            }
            if (fill)
                state.fill_color = this;
            else
                state.stroke_color = this;
        }

        #endregion

        #region Comparison

        public override int GetHashCode()
        {
            unchecked // Overflow isn't a problem
            {
                if (_color == null)
                    return _pattern.GetHashCode();

                int hash = 17;

                for (int c = 0; c < _color.Length; c++)
                    hash = hash * 23 + _color[c].GetHashCode();

                return hash;
            }
        }

        public bool Equals(double cyan, double magenta, double yellow, double black)
        {
            //Note that DeviceGray and DeviceRGB can match CMYK after color conversion, but
            //color conversion is too failure prone to bother with.
            return MyColorSpace is DeviceCMYK //No need to check for color != null.
                && cyan == _color[0] && magenta == _color[1] && yellow == _color[2] && (black == _color[3]);
        }

        public bool Equals(double red, double green, double blue)
        {
            //While comparing to gray is possible, it's impossible to know what RGB -> grayscale algorithim a
            //PDF viewer uses.
            return MyColorSpace is DeviceRGB &&
                red == _color[0] && green == _color[1] && blue == _color[2];
        }

        public bool Equals(double gray)
        {
            return MyColorSpace is DeviceGray && gray == _color[0];
        }

        public bool Equals(cBrush cs)
        {
            return ReferenceEquals(MyColorSpace, cs.MyColorSpace) && ReferenceEquals(_pattern, cs._pattern) &&
              Util.ArrayHelper.ArraysEqual<double>(_color, cs._color);
        }
        public override bool Equals(object obj)
        {
            return (obj is cBrush) ? Equals((cBrush)obj) : false;
        }
        public bool Equals(IColorSpace cs)
        {
            return MyColorSpace.Equals(cs);
        }

        public bool Equals(double[] color, PdfPattern pat)
        {
            return MyColorSpace is PatternCS && ReferenceEquals(pat, _pattern) && Equals(color);
        }

        public bool Equals(double[] color, CompiledPattern pat)
        {
            return MyColorSpace is CSPattern && ReferenceEquals(((CSPattern)MyColorSpace).CPat, pat) && Equals(color);
        }

        public bool Equals(double[] col)
        {
            if (col == null) return _color == null;
            if (_color == null || _color.Length != col.Length) return false;
            for (int c = 0; c < col.Length; c++)
                if (col[c] != _color[c])
                    return false;
            return true;
        }

        public static bool Equals(cBrush color1, cBrush color2)
        {
            // If both are null, or both are same instance, return true.
            if (ReferenceEquals(color1, color2))
                return true;

            // If one is null, but not both, return false.
            if (((object)color1 == null) || ((object)color2 == null))
                return false;

            return color1.Equals(color2);
        }

        public static bool operator ==(cBrush c1, cBrush c2)
        {
            return Similar(c1, c2);
        }
        public static bool operator !=(cBrush c1, cBrush c2)
        {
            return !Similar(c1, c2);
        }

        public static bool Similar(cBrush color1, cBrush color2)
        {
            // If both are null, or both are same instance, return true.
            if (ReferenceEquals(color1, color2))
                return true;

            // If one is null, but not both, return false.
            if (((object)color1 == null) || ((object)color2 == null))
                return false;

            if (color1.Equals(color2))
                return true;

            if (color1.MyColorSpace != null && color2.MyColorSpace != null &&
                color1._color != null && color2._color != null)
            {
                var c1 = color1.MyColorSpace.Converter.MakeColor(color1._color);
                var c2 = color2.MyColorSpace.Converter.MakeColor(color2._color);
                return PdfColor.AreSimilar(c1, c2);
            }

            if (color1._pattern != null)
                return ReferenceEquals(color1._pattern, color2._pattern);

            return false;
        }

        #endregion

        #region Classes

        public struct ColorRange
        {
            public readonly ColorStop From;
            public readonly ColorStop To;
            public ColorRange(ColorStop from, ColorStop to)
            {
                if (from == null || to == null)
                    throw new ArgumentNullException();
                if (from.Start > to.Start)
                    throw new PdfNotSupportedException("Reverse range");
                From = from; To = to;
            }
        }

        public class ColorStop
        {
            public readonly double Start;
            public readonly cColor Color;
            public ColorStop(double start, cColor color)
            {
                Start = start;
                if (color == null)
                    throw new ArgumentNullException();
                Color = color;
            }
        }

        #endregion
    }
}