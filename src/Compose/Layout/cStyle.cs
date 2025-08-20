using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using PdfLib.Pdf.ColorSpace;
using PdfLib.Compose.Layout.Internal;

namespace PdfLib.Compose.Layout
{
    /// <summary>
    /// The style information for a box.
    /// </summary>
    /// <remarks>
    /// This class uses relections to resolve properties along an inheritance chain.
    /// When adding new properties, you need to add them to both this class and the
    /// inner Resolver class, with the appropriate metadata. 
    /// 
    /// On second thought, instead of reflections this class 
    /// should be recoded around a dictionary.
    /// 
    /// To avoid frequent dictionary lookups, the resolver
    /// will then have to cache values.
    /// 
    /// Advantages:
    ///  - Adding more styles does not slow the implementations
    ///  - Code easier to maintain
    ///  - I don't like reflections
    ///  - Can be done without altering any users of these classes
    /// </remarks>
    public class cStyle
    {
        public event ChangeHandler StyleChanged;

        public delegate void ChangeHandler(cStyle style, ChangeType ct, string property);

        #region Apperance

        /// <summary>
        /// Whenever content is to be clipped or not
        /// </summary>
        public bool? Clip
        {
            get { return _clip; }
            set
            {
                if (_clip != value)
                {
                    _clip = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "Clip");
                }
            }
        }
        private bool? _clip = null;
        
        /// <summary>
        /// The containers background color, can be null
        /// </summary>
        public cBrush BackgroundColor
        {
            get { return _back_col; }
            set
            {
                if (_back_col != value || !cBrush.Equals(_back_col, value))
                {
                    _back_col = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "BackgroundColor");
                }
            }
        }

        /// <summary>
        /// Foreground color
        /// </summary>
        public cBrush Color
        {
            get { return _col; }
            set
            {
                if (_col != value || !cBrush.Equals(_col, value))
                {
                    _col = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "Color");
                }
            }
        }
        private cBrush _back_col, _col;

        private xDashStyle? _d_left, _d_right, _d_top, _d_bottom;
        public xDashStyle? DashLeft
        {
            get { return _d_left; }
            set
            {
                if (_d_left != value)
                {
                    _d_left = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "DashLeft");
                }
            }
        }
        public xDashStyle? DashRight
        {
            get { return _d_right; }
            set
            {
                if (_d_right != value)
                {
                    _d_right = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "DashRight");
                }
            }
        }
        public xDashStyle? DashTop
        {
            get { return _d_top; }
            set
            {
                if (_d_top != value)
                {
                    _d_top = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "DashTop");
                }
            }
        }
        public xDashStyle? DashBottom
        {
            get { return _d_bottom; }
            set
            {
                if (_d_bottom != value)
                {
                    _d_bottom = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "DashBottom");
                }
            }
        }

        [SkipProperty(ChangeType.Apperance, "DashLeft", "DashRight", "DashTop", "DashBottom")]
        public xDashStyle? DashStyle
        {
            get
            {
                if (xDashStyle.Equals(DashLeft, DashRight)
                    && xDashStyle.Equals(DashLeft, DashTop)
                    && xDashStyle.Equals(DashLeft, DashBottom))
                    return DashLeft;
                return null;
            }
            set
            {
                if (_d_left != value || _d_right != value || _d_top != value || _d_bottom != value)
                {
                    _d_left = value;
                    _d_right = value;
                    _d_top = value;
                    _d_bottom = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "DashStyle");
                }
            }
        }

        private cColor _bc_left, _bc_right, _bc_top, _bc_bottom;
        public cColor BorderColorLeft
        {
            get { return _bc_left; }
            set
            {
                if (!cColor.Equals(_bc_left, value))
                {
                    _bc_left = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "BorderColorLeft");
                }
            }
        }
        public cColor BorderColorRight
        {
            get { return _bc_right; }
            set
            {
                if (!cColor.Equals(_bc_right, value))
                {
                    _bc_right = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "BorderColorRight");
                }
            }
        }
        public cColor BorderColorTop
        {
            get { return _bc_top; }
            set
            {
                if (!cColor.Equals(_bc_top, value))
                {
                    _bc_top = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "BorderColorTop");
                }
            }
        }
        public cColor BorderColorBottom
        {
            get { return _bc_bottom; }
            set
            {
                if (!cColor.Equals(_bc_bottom, value))
                {
                    _bc_bottom = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "BorderColorBottom");
                }
            }
        }
        [SkipProperty(ChangeType.Apperance, "BorderColorLeft", "BorderColorRight", "BorderColorTop", "BorderColorBottom")]
        public cColor BorderColor
        {
            get
            {
                if (cColor.Equals(BorderColorLeft, BorderColorRight)
                    && cColor.Equals(BorderColorLeft, BorderColorTop)
                    && cColor.Equals(BorderColorLeft, BorderColorBottom))
                    return BorderColorLeft;
                return null;
            }
            set
            {
                if (BorderColor == null || !cColor.Equals(BorderColorLeft, value))
                {
                    _bc_left = value;
                    _bc_right = value;
                    _bc_top = value;
                    _bc_bottom = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "BorderColor");
                }
            }
        }

        //For this one must implement non-stroked border drawing. I.e. create the full geometry. 
        //
        // 1. Create the inner geomety, then set it as a clip path.
        // 2. After drawing background and text, pop it off
        // 3. Create geomety for top border, set it as clip path
        // 4. Draw the top border as normal, (stroked with xDashStyle set)
        // 5. Pop the clip off and continue with the next border.
        //
        //The biggest problem here is finding the mid point of curved paths.
        // http://stackoverflow.com/questions/8369488/splitting-a-bezier-curve
        //
        //There's no need to find the midpoint nor the tangent of the midpoint of the "stroke" line,
        //instead find midpoints of the inner and out bezier curves (inner being the curve on the
        //inside of the geometry, and outer being the outside.
        //
        //Finding the inner/outer beziers is simple. Just offset the, say, start point with half
        //the width of the border in the appropriate direction (y for horizontal borders, x for
        //vertical borders).

        private cSize? _RadiusLL, _RadiusLR, _RadiusUL, _RadiusUR;
        /// <summary>
        /// Border Radius Lower Left corner
        /// </summary>
        public cSize? BorderRadiusLL
        {
            get { return _RadiusLL; }
            set
            {
                if (_RadiusLL != value)
                {
                    _RadiusLL = (value == null) ? value : value.Value.Max(0);
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "BorderRadiusLL");
                }
            }
        }
        /// <summary>
        /// Border Radius Lower Right corner
        /// </summary>
        public cSize? BorderRadiusLR
        {
            get { return _RadiusLR; }
            set
            {
                if (_RadiusLR != value)
                {
                    _RadiusLR = (value == null) ? value : value.Value.Max(0);
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "BorderRadiusLR");
                }
            }
        }
        /// <summary>
        /// Border Radius Upper Left corner
        /// </summary>
        public cSize? BorderRadiusUL
        {
            get { return _RadiusUL; }
            set
            {
                if (_RadiusUL != value)
                {
                    _RadiusUL = (value == null) ? value : value.Value.Max(0);
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "BorderRadiusUL");
                }
            }
        }
        /// <summary>
        /// Border Radius Upper Right corner
        /// </summary>
        public cSize? BorderRadiusUR
        {
            get { return _RadiusUR; }
            set 
            {
                if (_RadiusUR != value)
                {
                    _RadiusUR = (value == null) ? value : value.Value.Max(0);
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "BorderRadiusUR");
                }
            }
        }
        [SkipProperty(ChangeType.Apperance, "BorderRadiusLL", "BorderRadiusLR", "BorderRadiusUL", "BorderRadiusUR")]
        public cSize? BorderRadius
        {
            get
            {
                if (_RadiusLL == _RadiusLR && _RadiusLL == _RadiusUL && _RadiusLL == _RadiusUR)
                    return _RadiusLL;
                return null;
            }
            set
            {
                cSize? val = value == null ? value : (value.Value).Max(0);
                if (BorderRadius == null || val != _RadiusLL)
                {
                    _RadiusLL = val;
                    _RadiusLR = val;
                    _RadiusUL = val;
                    _RadiusUR = val;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Apperance, "BorderRadius");
                }
            }
        }

        #endregion

        #region Position

        private ePosition? _position;
        public ePosition? Position
        {
            get { return _position; }
            set
            {
                if (value != _position)
                {
                    _position = value;
                    //Yes, ChangeType is size. See Resolver.Position for why.
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "Position");
                }
            }
        }

        private eHorizontalPos? _h_align;
        public eHorizontalPos? HorizontalAlignemt
        {
            get { return _h_align; }
            set
            {
                if (value != _h_align)
                {
                    _h_align = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Position, "HorizontalAlignemt");
                }
            }
        }

        private eVerticalPos? _v_align;
        public eVerticalPos? VerticalAlignemt
        {
            get { return _v_align; }
            set
            {
                if (value != _v_align)
                {
                    _v_align = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Position, "VerticalAlignemt");
                }
            }
        }

        private eLinePos? _l_align;
        public eLinePos? LineAlignemt
        {
            get { return _l_align; }
            set
            {
                if (value != _l_align)
                {
                    _l_align = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Position, "LineAlignemt");
                }
            }
        }

        /// <summary>
        /// Determines the absolute position.
        /// 
        /// In css, this is also used to determine widht and height if widht and height does
        /// not exist. This is certainly possible to do, but is for now not supported.
        /// 
        /// To support such, the Resolver must be modified to tell if width and height is
        /// truly set (or perhaps asumme such when unit auto). Then DoLayout must be
        /// modified to use these values.
        /// 
        /// 2 further problems: "size dep on parent" flag must also be set,
        /// finally layout must be invalidated when these values change. 
        /// </summary>
        private cSize? _left, _right, _top, _bottom;
        public cSize? Left
        {
            get { return _left; }
            set
            {
                if (_left != value)
                {
                    _left = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Position, "Left");
                }
            }
        }
        public cSize? Right
        {
            get { return _right; }
            set
            {
                if (_right != value)
                {
                    _right = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Position, "Right");
                }
            }
        }
        public cSize? Top
        {
            get { return _top; }
            set
            {
                if (_top != value)
                {
                    _top = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Position, "Top");
                }
            }
        }
        public cSize? Bottom
        {
            get { return _bottom; }
            set
            {
                if (_bottom != value)
                {
                    _bottom = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Position, "Bottom");
                }
            }
        }


        /// <summary>
        /// Rotates the box
        /// </summary>
        private double? _rot_angle;
        public double? RotationAngle
        {
            get { return _rot_angle; }
            set
            {
                if (value != _rot_angle)
                {
                    _rot_angle = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Position, "RotationAngle");
                }
            }
        }

        /// <summary>
        /// Point to rotate the box around. By default, center point is used.
        /// For upper right, set (1,1), lower left, (0,0) etc.
        /// </summary>
        private xPoint? _rot_point;
        public xPoint? RotationPoint 
        { 
            get { return _rot_point; } 
            set 
            {
                if (_rot_point != value)
                {
                    _rot_point = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Position, "RotationPoint");
                }
            } 
        }

        #endregion

        #region Size

        private eBoxSizing? _box_sizing;
        public eBoxSizing? Sizing
        {
            get { return _box_sizing; }
            set
            {
                if (_box_sizing != value)
                {
                    _box_sizing = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "Sizing");
                }
            }
        }

        private cSize? _width, _height;
        public cSize? Width
        {
            get { return _width; }
            set
            {
                if (_width != value)
                {
                    _width = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "Width");
                }
            }
        }
        public cSize? Height
        {
            get { return _height; }
            set
            {
                if (_height != value)
                {
                    _height = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "Height");
                }
            }
        }
        
        /// <summary>
        /// How the box will be layed out when inside another box
        /// </summary>
        private eDisplay? _display;
        public eDisplay? Display
        {
            get { return _display; }
            set
            {
                if (_display != value)
                {
                    _display = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "Display");
                }
            }
        }

        private cSize? _m_left, _m_right, _m_top, _m_bottom;
        public cSize? MarginLeft
        {
            get { return _m_left; }
            set
            {
                if (_m_left != value)
                {
                    _m_left = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "MarginLeft");
                }
            }
        }
        public cSize? MarginRight
        {
            get { return _m_right; }
            set
            {
                if (_m_right != value)
                {
                    _m_right = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "MarginRight");
                }
            }
        }
        public cSize? MarginTop
        {
            get { return _m_top; }
            set
            {
                if (_m_top != value)
                {
                    _m_top = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "MarginTop");
                }
            }
        }
        public cSize? MarginBottom
        {
            get { return _m_bottom; }
            set
            {
                if (_m_bottom != value)
                {
                    _m_bottom = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "MarginBottom");
                }
            }
        }
        /// <summary>
        /// Space arround the box.
        /// </summary>
        [SkipProperty(ChangeType.Size, "MarginLeft", "MarginRight", "MarginTop", "MarginBottom")]
        public cSize? Margin
        {
            get
            {
                if (MarginLeft == MarginRight && MarginLeft == MarginTop && MarginLeft == MarginBottom)
                    return MarginLeft;
                return null;
            }
            set
            {
                cSize val = value == null ? new cSize() : (value.Value).Max(0);
                if (Margin == null || val != MarginLeft)
                {
                    MarginLeft = val;
                    MarginRight = val;
                    MarginTop = val;
                    MarginBottom = val;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "Margin");
                }
            }
        }

        private cSize? _p_left, _p_right, _p_top, _p_bottom;
        public cSize? PaddingLeft
        {
            get { return _p_left; }
            set
            {
                if (_p_left != value)
                {
                    _p_left = value != null ? value.Value.Max(0) : (cSize?) null;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "PaddingLeft");
                }
            }
        }
        public cSize? PaddingRight
        {
            get { return _p_right; }
            set
            {
                if (_p_right != value)
                {
                    _p_right = value != null ? value.Value.Max(0) : (cSize?)null;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "PaddingRight");
                }
            }
        }
        public cSize? PaddingTop
        {
            get { return _p_top; }
            set
            {
                if (_p_top != value)
                {
                    _p_top = value != null ? value.Value.Max(0) : (cSize?)null;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "PaddingTop");
                }
            }
        }
        public cSize? PaddingBottom
        {
            get { return _p_bottom; }
            set
            {
                if (_p_bottom != value)
                {
                    _p_bottom = value != null ? value.Value.Max(0) : (cSize?)null;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "PaddingBottom");
                }
            }
        }
        /// <summary>
        /// Space inside the box.
        /// </summary>
        [SkipProperty(ChangeType.Size, "PaddingLeft", "PaddingRight", "PaddingTop", "PaddingBottom")]
        public cSize? Padding
        {
            get
            {
                if (PaddingLeft == PaddingRight && PaddingLeft == PaddingTop && PaddingLeft == PaddingBottom)
                    return PaddingLeft;
                return null;
            }
            set
            {
                cSize val = value == null ? new cSize() : (value.Value).Max(0);
                if (Padding == null || val != PaddingLeft)
                {
                    PaddingLeft = val;
                    PaddingRight = val;
                    PaddingTop = val;
                    PaddingBottom = val;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "Padding");
                }
            }
        }

        private cSize? _BLeft, _BRight, _BTop, _BBottom;
        /// <summary>
        /// Left border width
        /// </summary>
        public cSize? BorderLeft
        {
            get { return _BLeft; }
            set
            {
                if (_BLeft != value)
                {
                    _BLeft = value != null ? value.Value.Max(0) : (cSize?)null;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "BorderLeft");
                }
            }
        }
        /// <summary>
        /// Right border width
        /// </summary>
        public cSize? BorderRight
        {
            get { return _BRight; }
            set
            {
                if (_BRight != value)
                {
                    _BRight = value != null ? value.Value.Max(0) : (cSize?)null;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "BorderRight");
                }
            }
        }
        /// <summary>
        /// Top border width
        /// </summary>
        public cSize? BorderTop
        {
            get { return _BTop; }
            set
            {
                if (_BTop != value)
                {
                    _BTop = value != null ? value.Value.Max(0) : (cSize?)null;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "BorderTop");
                }
            }
        }
        /// <summary>
        /// Bottom border width
        /// </summary>
        public cSize? BorderBottom
        {
            get { return _BBottom; }
            set 
            {
                if (_BBottom != value)
                {
                    _BBottom = value != null ? value.Value.Max(0) : (cSize?)null;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "BorderBottom");
                }
            }
        }
        [SkipProperty(ChangeType.Size, "BorderLeft", "BorderRight", "BorderTop", "BorderBottom")]
        public cSize? BorderTichness
        {
            get
            {
                if (_BLeft == _BRight && _BLeft == _BTop && _BLeft == _BBottom)
                    return _BLeft;
                return null;
            }
            set
            {
                cSize val = value == null ? new cSize() : (value.Value).Max(0);
                if (BorderTichness == null || val != _BLeft)
                {
                    _BLeft = val;
                    _BRight = val;
                    _BTop = val;
                    _BBottom = val;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "BorderTichness");
                }
            }
        }

        #endregion

        #region Text

        private cSize? _font_size;
        private cFont _font;
        private double? _line_height;
        private eTextAlignement? _text_alignement;
        private cTextSpacing? _text_spacing;
        private eWhiteSpace? _white_space;

        /// <summary>
        /// Size of the text
        /// </summary>
        public cSize? FontSize
        {
            get { return _font_size; }
            set
            {
                if (_font_size != value)
                {
                    if (value == null)
                        _font_size = null;
                    else
                    {
                        var unit = value.Value.Unit;
                        if (unit == cUnit.Precentage)
                            throw new NotImplementedException("Font is to be sized in relation to the current font size.");
                        if (unit == cUnit.Auto)
                            throw new NotSupportedException("Fonts can not be automatically sized.");

                        _font_size = value.Value.Max(0);
                    }
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "FontSize");
                }
            }
        }

        /// <summary>
        /// What font to use
        /// </summary>
        public cFont Font
        {
            get { return _font; }
            set
            {
                if (!ReferenceEquals(_font, value))
                {
                    _font = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "Font");
                }
            }
        }

        public double? LineHeight
        {
            get { return _line_height; }
            set
            {
                if (!Util.Real.Same(value, _line_height))
                {
                    _line_height = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "LineHeight");
                }
            }
        }

        public eWhiteSpace? WhiteSpace
        {
            get { return _white_space; }
            set
            {
                if (value != _white_space)
                {
                    _white_space = value;
                    StyleChanged?.Invoke(this, ChangeType.Size, "WhiteSpace");
                }
            }
        }

        public cTextSpacing? TextSpacing
        {
            get { return _text_spacing; }
            set
            {
                if (value != _text_spacing)
                {
                    _text_spacing = value;
                    StyleChanged?.Invoke(this, ChangeType.Size, "TextSpacing");
                }
            }
        }

        /// <summary>
        /// How text is positioned inside a text box.
        /// </summary>
        public eTextAlignement? TextAlignement
        {
            get { return _text_alignement; }
            set
            {
                if (value != _text_alignement)
                {
                    _text_alignement = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Position, "TextAlignement");
                }
            }
        }

        #endregion

        #region Table

        private cSize? _border_spacing;
        private eBorderCollapse? _border_collapse;
        private eTableLayout? _table_layout;

        public eTableLayout? TableLayout
        {
            get { return _table_layout; }
            set
            {
                if (value != _table_layout)
                {
                    _table_layout = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "TableLayout");
                }
            }
        }

        public cSize? BorderSpacing
        {
            get { return _border_spacing; }
            set
            {
                if (value != _border_spacing)
                {
                    if (value != null && !value.Value.IsFirm)
                        throw new PdfNotSupportedException("Size must be given in firm units");
                    _border_spacing = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "BorderSpacing");
                }
            }
        }

        public eBorderCollapse? BorderCollapse
        {
            get { return _border_collapse; }
            set
            {
                if (value != _border_collapse)
                {
                    _border_collapse = value;
                    if (StyleChanged != null)
                        StyleChanged(this, ChangeType.Size, "BorderCollapse");
                }
            }
        }

        #endregion

        #region Init

        /// <summary>
        /// A style can have a single parent style, which this
        /// style then inherit from. 
        /// </summary>
        /// <remarks>Needs to be readonly due to implementation details.</remarks>
        private readonly cStyle ParentStyle;

        public cStyle()
        { }

        public cStyle(cStyle parent)
        {
            ParentStyle = parent;
        }

        #endregion

        #region Enumerations and attributes

        public enum ChangeType
        {
            Unknown = -1,
            Apperance,
            Position,
            Size
        }

        public enum ChangeProperty
        {
            Clip,
            BackgroundColor,
            DashStyle,
            BorderColor,
            BorderRadius,
            RotationAngle,
            RotationPoint,
            Sizing,
            Width,
            Height,
            Display,
            Margin,
            Padding,
            BorderTichness
        }       

        class SkipPropertyAttribute : Attribute
        {
            public readonly string[] Fields;
            public readonly cStyle.ChangeType StyleType;

            public SkipPropertyAttribute()
            { }
            public SkipPropertyAttribute(cStyle.ChangeType type, params string[] fields)
            {
                StyleType = type;
                Fields = fields;
            }
        }

        #endregion

        /// <summary>
        /// Resolves style information, so that a box can have
        /// multiple styles.
        /// </summary>
        /// <remarks>
        /// This class exists for convinience more than anything. All
        /// boxes has one style resolver, and they can not be shared
        /// 
        /// For now, styles can not be set. To change this, say, 
        ///  1. add nullable field called inline_xxx
        ///  2. add logic for resolve functions that checks this inline field first/last.
        ///  
        /// Uses reflections to reduce the amount of code I got to write. 
        /// </remarks>
        public class Resolver
        {
            #region Fields

            private readonly List<cStyle> _styles = new List<cStyle>(1);
            private readonly cStyle.ChangeHandler _change;
            private readonly cBox _owner;
            internal cBox Owner { get { return _owner; } }
            public delegate void ChangeHandler(Resolver style, ChangeType ct, string property);
            public event ChangeHandler StyleChanged;

            /// <summary>
            /// Dictionary over default values
            /// </summary>
            private Dictionary<string, object> _def;

            #region MetaData

            private class PropertyMeta
            {
                public readonly object Default;
                public PropertyMeta(object def_val)
                {
                    Default = def_val;
                }
            }
            static readonly Dictionary<string, PropertyMeta> _meta;

            #endregion

            #endregion

            public void AddDefault(string property, object value)
            {
                if (string.IsNullOrWhiteSpace(property))
                    return;

                if (_def == null)
                    _def = new Dictionary<string, object>();
                _def["%" + property] = value;
            }

            private void Reset()
            {
                _Clip = false;
                _BackgroundColor = null;
                _Color = null;
                _DashLeft = _DashBottom = _DashRight = _DashTop = null;
                _BorderColorLeft = _BorderColorRight = _BorderColorTop = _BorderColorBottom = null;
                _BorderRadiusLL = _BorderRadiusLR = _BorderRadiusUL = _BorderRadiusUR = new cSize();
                _RotationAngle = 0;
                _RotationPoint = new xPoint(.5, .5);
                _Position = ePosition.Static;
                _Sizing = eBoxSizing.BorderBox;
                _Width = null;
                _Height = new cSize(0, cUnit.Auto);
                _Display = eDisplay.Block;
                _MarginLeft = _MarginRight = _MarginTop = _MarginBottom = new cSize();
                _PaddingLeft = _PaddingRight = _PaddingTop = _PaddingBottom = new cSize();
                _BorderLeft = _BorderRight = _BorderTop = _BorderBottom = new cSize();
                _Left = _Right = _Top = _Bottom = new cSize(float.NaN);
                _HorizontalAlignemt = eHorizontalPos.Left;
                _VerticalAlignemt = eVerticalPos.Top;
                _LineAlignemt = eLinePos.Top;

                _Font = cFont.Times;
                _FontSize = new cSize(16);
                _LineHeight = null;
                _TextAlignement = eTextAlignement.Descent;
                _TextSpacing = new cTextSpacing(0, 0, 100);
                _WhiteSpace = eWhiteSpace.pre;

                _TableLayout = eTableLayout.Auto;
                _BorderCollapse = eBorderCollapse.separate;
                _BorderSpacing = new cSize(0);

                if (_def != null)
                {
                    foreach(var kv in _def)
                    {
                        if (kv.Key[0] == '%')
                        {
                            var property_name = kv.Key.Substring(1);
                            var field = typeof(Resolver).GetField("_" + property_name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            if (field != null)
                                field.SetValue(this, kv.Value);
                        }
                    }
                }
            }

            #region Apperance

            [IncludeProperty(ChangeType.Apperance, "Clip")]
            private bool _Clip;
            public bool Clip { get { return _Clip; } }

            [IncludeProperty(ChangeType.Apperance, "BackgroundColor")]
            private cBrush _BackgroundColor;
            public cBrush BackgroundColor { get { return _BackgroundColor; } }

            [IncludeProperty(ChangeType.Apperance, "Color")]
            private cBrush _Color;
            public cBrush Color { get { return _Color; } }

            [IncludeProperty(ChangeType.Apperance, "DashLeft")]
            private xDashStyle? _DashLeft;
            public xDashStyle? DashLeft { get { return _DashLeft; } }
            [IncludeProperty(ChangeType.Apperance, "DashRight")]
            private xDashStyle? _DashRight;
            public xDashStyle? DashRight { get { return _DashRight; } }
            [IncludeProperty(ChangeType.Apperance, "DashTop")]
            private xDashStyle? _DashTop;
            public xDashStyle? DashTop { get { return _DashTop; } }
            [IncludeProperty(ChangeType.Apperance, "DashBottom")]
            private xDashStyle? _DashBottom;
            public xDashStyle? DashBottom { get { return _DashBottom; } }
            public xDashStyle? DashStyle
            {
                get
                {
                    if (xDashStyle.Equals(DashLeft, DashRight)
                        && xDashStyle.Equals(DashLeft, DashTop)
                        && xDashStyle.Equals(DashLeft, DashBottom))
                        return DashLeft;
                    return null;
                }
            }

            [IncludeProperty(ChangeType.Apperance, "BorderColorLeft")]
            private cColor _BorderColorLeft;
            public cColor BorderColorLeft { get { return _BorderColorLeft; } }
            [IncludeProperty(ChangeType.Apperance, "BorderColorRight")]
            private cColor _BorderColorRight;
            public cColor BorderColorRight { get { return _BorderColorRight; } }
            [IncludeProperty(ChangeType.Apperance, "BorderColorTop")]
            private cColor _BorderColorTop;
            public cColor BorderColorTop { get { return _BorderColorTop; } }
            [IncludeProperty(ChangeType.Apperance, "BorderColorBottom")]
            private cColor _BorderColorBottom;
            public cColor BorderColorBottom { get { return _BorderColorBottom; } }
            public cColor BorderColor
            {
                get
                {
                    if (cColor.Equals(BorderColorLeft, BorderColorRight)
                        && cColor.Equals(BorderColorLeft, BorderColorTop)
                        && cColor.Equals(BorderColorLeft, BorderColorBottom))
                        return BorderColorLeft;
                    return null;
                }
            }

            [IncludeProperty(ChangeType.Apperance, "BorderRadiusLL")]
            private cSize _BorderRadiusLL;
            public cSize BorderRadiusLL { get { return _BorderRadiusLL; } }
            [IncludeProperty(ChangeType.Apperance, "BorderRadiusLR")]
            private cSize _BorderRadiusLR;
            public cSize BorderRadiusLR { get { return _BorderRadiusLR; } }
            [IncludeProperty(ChangeType.Apperance, "BorderRadiusUL")]
            private cSize _BorderRadiusUL;
            public cSize BorderRadiusUL { get { return _BorderRadiusUL; } }
            [IncludeProperty(ChangeType.Apperance, "BorderRadiusUR")]
            private cSize _BorderRadiusUR;
            public cSize BorderRadiusUR { get { return _BorderRadiusUR; } }
            public cSize? BorderRadius
            {
                get
                {
                    if (BorderRadiusLL == BorderRadiusLR && BorderRadiusLL == BorderRadiusUL && BorderRadiusLL == BorderRadiusUR)
                        return BorderRadiusLL;
                    return null;
                }
            }

            #endregion

            #region Position

            //Yes, changetype is size. This because size might "change" to
            //     0,0 from the parent's perspective.
            //
            //Since layout > position, we don't have to notify that the position
            //changes when we set the "need layout" flag.
            //
            //Alt. Introduce special handeling for the events. 
            [IncludeProperty(ChangeType.Size, "Position")]
            private ePosition _Position;
            public ePosition Position { get { return _Position; } }

            private eHorizontalPos _HorizontalAlignemt;
            public eHorizontalPos HorizontalAlignemt { get { return _HorizontalAlignemt; } }
            private eVerticalPos _VerticalAlignemt;
            public eVerticalPos VerticalAlignemt { get { return _VerticalAlignemt; } }
            private eLinePos _LineAlignemt;
            public eLinePos LineAlignemt { get { return _LineAlignemt; } }

            [IncludeProperty(ChangeType.Position, "Left")]
            private cSize _Left;
            public cSize Left { get { return _Left; } }
            [IncludeProperty(ChangeType.Position, "Right")]
            private cSize _Right;
            public cSize Right { get { return _Right; } }
            [IncludeProperty(ChangeType.Position, "Top")]
            private cSize _Top;
            public cSize Top { get { return _Top; } }
            [IncludeProperty(ChangeType.Position, "Bottom")]
            private cSize _Bottom;
            public cSize Bottom { get { return _Bottom; } }

            [IncludeProperty(ChangeType.Position, "RotationAngle")]
            private double _RotationAngle;
            public double RotationAngle { get { return _RotationAngle; } }

            [IncludeProperty(ChangeType.Position, "RotationPoint")]
            private xPoint _RotationPoint;
            public xPoint RotationPoint { get { return _RotationPoint; } }

            #endregion

            #region Size

            [IncludeProperty(ChangeType.Size, "Sizing")]
            private eBoxSizing _Sizing;
            public eBoxSizing Sizing { get { return _Sizing; } }

            /// <summary>
            /// Width is a bit odd. When there's no width, sizing is to be
            /// done using different calcualtions than where there is width.
            /// 
            /// Publically we reprecent this is NaN %, while privatly we use
            /// a null. That way, if a user sets NaN %, we are not strictly
            /// fooled. 
            /// </summary>
            [IncludeProperty(ChangeType.Size, "Width")]
            private cSize? _Width;

            /// <summary>
            /// Width if the box.
            /// 
            /// If width is NaN precent, it means that the box has a width equal to
            /// 100% of parent, substracting the margins of this box.
            /// </summary>
            public cSize Width { get { return _Width == null ? Display == eDisplay.Block ? new cSize(float.NaN, cUnit.Precentage) : new cSize(0, cUnit.Auto) : _Width.Value; } }

            [IncludeProperty(ChangeType.Size, "Height")]
            private cSize _Height;
            public cSize Height { get { return _Height; } }

            [IncludeProperty(ChangeType.Size, "Display")]
            private eDisplay _Display;
            public eDisplay Display { get { return _Display; } }

            [IncludeProperty(ChangeType.Size, "MarginLeft")]
            private cSize _MarginLeft;
            public cSize MarginLeft { get { return _MarginLeft; } }
            [IncludeProperty(ChangeType.Size, "MarginRight")]
            private cSize _MarginRight;
            public cSize MarginRight { get { return _MarginRight; } }
            [IncludeProperty(ChangeType.Size, "MarginTop")]
            private cSize _MarginTop;
            public cSize MarginTop { get { return _MarginTop; } }
            [IncludeProperty(ChangeType.Size, "MarginBottom")]
            private cSize _MarginBottom;
            public cSize MarginBottom { get { return _MarginBottom; } }
            public cSize? Margin
            {
                get
                {
                    if (_MarginLeft == _MarginRight && _MarginLeft == _MarginTop && _MarginLeft == _MarginBottom)
                        return _MarginLeft;
                    return null;
                }
            }

            [IncludeProperty(ChangeType.Size, "PaddingLeft")]
            private cSize _PaddingLeft;
            public cSize PaddingLeft { get { return _PaddingLeft; } }
            [IncludeProperty(ChangeType.Size, "PaddingRight")]
            private cSize _PaddingRight;
            public cSize PaddingRight { get { return _PaddingRight; } }
            [IncludeProperty(ChangeType.Size, "PaddingTop")]
            private cSize _PaddingTop;
            public cSize PaddingTop { get { return _PaddingTop; } }
            [IncludeProperty(ChangeType.Size, "PaddingBottom")]
            private cSize _PaddingBottom;
            public cSize PaddingBottom { get { return _PaddingBottom; } }
            public cSize? Padding
            {
                get
                {
                    if (_PaddingLeft == _PaddingRight && _PaddingLeft == _PaddingTop && _PaddingLeft == _PaddingBottom)
                        return _PaddingLeft;
                    return null;
                }
            }

            [IncludeProperty(ChangeType.Size, "BorderLeft")]
            private cSize _BorderLeft;
            public cSize BorderLeft { get { return _BorderLeft; } }
            [IncludeProperty(ChangeType.Size, "BorderRight")]
            private cSize _BorderRight;
            public cSize BorderRight { get { return _BorderRight; } }
            [IncludeProperty(ChangeType.Size, "BorderTop")]
            private cSize _BorderTop;
            public cSize BorderTop { get { return _BorderTop; } }
            [IncludeProperty(ChangeType.Size, "BorderBottom")]
            private cSize _BorderBottom;
            public cSize BorderBottom { get { return _BorderBottom; } }
            public cSize? BorderTichness
            {
                get
                {
                    if (_BorderLeft == _BorderRight && _BorderLeft == _BorderTop && _BorderLeft == _BorderBottom)
                        return _BorderLeft;
                    return null;
                }
            }

            #endregion

            #region Text

            [IncludeProperty(ChangeType.Size, "FontSize")]
            private cSize _FontSize;
            public cSize FontSize { get { return _FontSize; } }

            [IncludeProperty(ChangeType.Size, "Font")]
            private cFont _Font;
            public cFont Font { get { return _Font; } }

            [IncludeProperty(ChangeType.Size, "LineHeight")]
            private double? _LineHeight;
            public double? LineHeight { get { return _LineHeight; } }

            [IncludeProperty(ChangeType.Size, "TextSpacing")]
            private cTextSpacing _TextSpacing;
            public cTextSpacing TextSpacing { get { return _TextSpacing; } }

            [IncludeProperty(ChangeType.Size, "WhiteSpace")]
            private eWhiteSpace _WhiteSpace;
            public eWhiteSpace WhiteSpace { get { return _WhiteSpace; } }

            [IncludeProperty(ChangeType.Position, "TextAlignement")]
            private eTextAlignement _TextAlignement;
            public eTextAlignement TextAlignement { get { return _TextAlignement; } }

            #endregion

            #region Table

            [IncludeProperty(ChangeType.Size, "TableLayout")]
            private eTableLayout _TableLayout;
            public eTableLayout TableLayout { get { return _TableLayout; } }

            [IncludeProperty(ChangeType.Size, "BorderCollapse")]
            private eBorderCollapse _BorderCollapse;
            public eBorderCollapse BorderCollapse { get { return _BorderCollapse; } }

            [IncludeProperty(ChangeType.Size, "BorderSpacing")]
            private cSize _BorderSpacing;
            public cSize BorderSpacing { get { return _BorderSpacing; } }

            #endregion

            #region Init

            internal Resolver(cStyle style, cBox owner)
            {
                _change = new cStyle.ChangeHandler(style_StyleChanged);
                AddStyle(style);
                _owner = owner;
            }

            readonly static System.Reflection.PropertyInfo[] _style_types;
            readonly static System.Reflection.FieldInfo[] _types;
            static Resolver()
            {
                //Fetches reflection related meta data and filters it
                var types = typeof(cStyle).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.GetProperty);
                _style_types = Array.FindAll(types, t => !Attribute.IsDefined(t, typeof(SkipPropertyAttribute)));
                var fields = typeof(Resolver).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                _types = Array.FindAll(fields, f => Attribute.IsDefined(f, typeof(IncludePropertyAttribute)));

                _meta = new Dictionary<string, PropertyMeta>(48);
                _meta.Add("Clip", new PropertyMeta(false)); //1
                _meta.Add("BackgroundColor", new PropertyMeta(null)); //2
                _meta.Add("Color", new PropertyMeta(null)); //3
                _meta.Add("DashLeft", new PropertyMeta(null)); //4
                _meta.Add("DashBottom", new PropertyMeta(null)); //5
                _meta.Add("DashRight", new PropertyMeta(null)); //6
                _meta.Add("DashTop", new PropertyMeta(null)); //7
                _meta.Add("BorderColorLeft", new PropertyMeta(null)); //8
                _meta.Add("BorderColorRight", new PropertyMeta(null)); //9
                _meta.Add("BorderColorTop", new PropertyMeta(null)); //10
                _meta.Add("BorderColorBottom", new PropertyMeta(null)); //11
                _meta.Add("BorderRadiusLL", new PropertyMeta(new cSize())); //12
                _meta.Add("BorderRadiusLR", new PropertyMeta(new cSize())); //13
                _meta.Add("BorderRadiusUL", new PropertyMeta(new cSize())); //14
                _meta.Add("BorderRadiusUR", new PropertyMeta(new cSize())); //15
                _meta.Add("RotationAngle", new PropertyMeta(0d)); //16
                _meta.Add("RotationPoint", new PropertyMeta(new xPoint(.5, .5))); //17
                _meta.Add("Position", new PropertyMeta(ePosition.Static)); //18
                _meta.Add("Sizing", new PropertyMeta(eBoxSizing.BorderBox)); //19
                _meta.Add("Width", new PropertyMeta(null)); //20
                _meta.Add("Height", new PropertyMeta(new cSize(0, cUnit.Auto))); //20
                _meta.Add("Display", new PropertyMeta(eDisplay.Block)); //21
                _meta.Add("MarginLeft", new PropertyMeta(new cSize())); //22
                _meta.Add("MarginRight", new PropertyMeta(new cSize())); //23
                _meta.Add("MarginTop", new PropertyMeta(new cSize())); //24
                _meta.Add("MarginBottom", new PropertyMeta(new cSize())); //25
                _meta.Add("PaddingLeft", new PropertyMeta(new cSize())); //26
                _meta.Add("PaddingRight", new PropertyMeta(new cSize())); //27
                _meta.Add("PaddingTop", new PropertyMeta(new cSize())); //28
                _meta.Add("PaddingBottom", new PropertyMeta(new cSize())); //29
                _meta.Add("BorderLeft", new PropertyMeta(new cSize())); //30
                _meta.Add("BorderRight", new PropertyMeta(new cSize())); //31
                _meta.Add("BorderTop", new PropertyMeta(new cSize())); //32
                _meta.Add("BorderBottom", new PropertyMeta(new cSize())); //33
                _meta.Add("Left", new PropertyMeta(new cSize(float.NaN))); //34
                _meta.Add("Right", new PropertyMeta(new cSize(float.NaN))); //35
                _meta.Add("Top", new PropertyMeta(new cSize(float.NaN))); //36
                _meta.Add("Bottom", new PropertyMeta(new cSize(float.NaN))); //37
                _meta.Add("HorizontalAlignemt", new PropertyMeta(eHorizontalPos.Left)); //38
                _meta.Add("VerticalAlignemt", new PropertyMeta(eVerticalPos.Top)); //39
                _meta.Add("LineAlignemt", new PropertyMeta(eLinePos.Top)); //40
                _meta.Add("Font", new PropertyMeta(cFont.Times)); //41
                _meta.Add("FontSize", new PropertyMeta(new cSize(16))); //42
                _meta.Add("LineHeight", new PropertyMeta(null)); //43
                _meta.Add("TextAlignement", new PropertyMeta(eTextAlignement.Descent)); //44
                _meta.Add("BorderCollapse", new PropertyMeta(eBorderCollapse.separate)); //45
                _meta.Add("BorderSpacing", new PropertyMeta(new cSize(0))); //46
                _meta.Add("TableLayout", new PropertyMeta(eTableLayout.Auto)); //47
                _meta.Add("TextSpacing", new PropertyMeta(new cTextSpacing(0, 0, 100))); //48
            }

            #endregion

            public void Add(cStyle style)
            {
                AddStyle(style);
                if (_owner.HasListeners)
                {
                    style.StyleChanged += _change;
                    ResolveFrom(_styles.Count - 1);
                }
            }

            private void AddStyle(cStyle style)
            {
                if (style.ParentStyle != null)
                    AddStyle(style.ParentStyle);
                _styles.Add(style);
            }

            #region Events

            /// <summary>
            /// For when a box is added to a tree for the first time.
            /// </summary>
            internal void AddListeners()
            {
                foreach (var style in _styles)
                    style.StyleChanged += _change;
                Reset();
                Resolve();
            }

            /// <summary>
            /// For when a box has been removed and readded to the three
            /// </summary>
            /// <returns>True if style changed</returns>
            internal bool ReAddListeners()
            {
                foreach (var style in _styles)
                    style.StyleChanged += _change;

                var clone = (Resolver)MemberwiseClone();
                Reset();
                Resolve();

                bool style_changed = false;
                foreach (var field in _types)
                {
                    var org_val = field.GetValue(clone);
                    var new_val = field.GetValue(this);
                    if (org_val != new_val && (org_val == null || !org_val.Equals(new_val)))
                    {
                        var atr = (IncludePropertyAttribute)Attribute.GetCustomAttribute(field, typeof(IncludePropertyAttribute));
                        //FireStyleChanged(atr.StyleType, atr.PropertyName);
                        //if (atr.StyleType == ChangeType.Size)
                            style_changed = true;
                    }
                }
                return style_changed;
            }

            internal void RemoveListeners()
            {
                foreach (var style in _styles)
                    style.StyleChanged -= _change;
            }

            void style_StyleChanged(cStyle style, cStyle.ChangeType ct, string property_name)
            {
                Resolve(property_name, ct);
            }

            void FireStyleChanged(ChangeType ct, string cp)
            {
                if (StyleChanged != null)
                    StyleChanged(this, ct, cp);
            }

            #endregion

            #region Resolve functions

            /// <summary>
            /// For reading properties when there's no listeners.
            /// </summary>
            /// <param name="property">Property to respolve</param>
            /// <param name="ct">Change type, set to unknown to not fire events</param>
            internal void Resolve(string property_name, ChangeType ct = ChangeType.Unknown)
            {
                var field = typeof(Resolver).GetField("_" + property_name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var prop = typeof(cStyle).GetProperty(property_name);

                if (field != null)
                {
                    //Finds the value for this field. Last stylesheet gets preference.
                    object new_val = null;
                    int len = _styles.Count;
                    for (var count = len - 1; count >= 0; count--)
                    {
                        var a_style = _styles[count];
                        var s_val = prop.GetValue(a_style, null);
                        if (s_val != null)
                        {
                            new_val = s_val;
                            break;
                        }
                    }

                    //Gets the current value
                    var val = field.GetValue(this);

                    if (new_val == null && val != null)
                    {
                        //Sets the default value
                        new_val = _meta[property_name].Default;
                    }

                    //Unequal check
                    if (val != new_val && (val == null || !val.Equals(new_val)))
                    {
                        //Sets the value
                        field.SetValue(this, new_val);

                        if (ct == ChangeType.Unknown)
                        {
                            //var atr = (IncludePropertyAttribute)Attribute.GetCustomAttribute(field, typeof(IncludePropertyAttribute));
                            //if (atr != null)
                            //    ct = atr.StyleType;
                        }
                        else
                            FireStyleChanged(ct, property_name);
                    }
                }
                else if (prop != null && Attribute.IsDefined(prop, typeof(SkipPropertyAttribute)))
                {
                    //This is a property that sets many fields. 
                    var atr = (SkipPropertyAttribute)Attribute.GetCustomAttribute(prop, typeof(SkipPropertyAttribute));
                    if (atr != null && atr.Fields != null)
                    {
                        string[] fields = atr.Fields;
                        object[] new_values = new object[fields.Length];
                        var props = new System.Reflection.PropertyInfo[fields.Length];
                        for (int c = 0; c < props.Length; c++)
                            props[c] = typeof(cStyle).GetProperty(fields[c]);

                        int len = _styles.Count;
                        for (var count = 0; count < len; count++)
                        {
                            var a_style = _styles[count];
                            for (int c = 0; c < fields.Length; c++)
                            {
                                var s_val = props[c].GetValue(a_style, null);
                                if (s_val != null)
                                    new_values[c] = s_val;
                            }
                        }

                        bool fire_event = false;
                        for (int c = 0; c < new_values.Length; c++)
                        {
                            var a_field = typeof(Resolver).GetField("_" + fields[c], System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            if (a_field != null)
                            {
                                var old_val = a_field.GetValue(this);
                                var new_val = new_values[c];

                                //Unequal check
                                if (old_val != new_val && (old_val == null || !old_val.Equals(new_val)))
                                {
                                    //Sets the value
                                    a_field.SetValue(this, new_val);
                                    fire_event = true;
                                }
                            }
                        }
                        if (fire_event && ct != ChangeType.Unknown)
                        {
                            ct = atr.StyleType;
                            FireStyleChanged(ct, property_name);
                        }
                    }
                }
            }

            /// <summary>
            /// Resolves without fiering listeners
            /// </summary>
            internal void ResolveAll()
            {
                Reset();
                Resolve();
            }

            /// <summary>
            /// Resolves without fiering listeners
            /// </summary>
            /// <param name="start">Style to start from</param>
            private void Resolve(int start = 0)
            {
                var t = typeof(cStyle.Resolver);

                int len = _styles.Count;
                for (int count = start; count < len; count++)
                {
                    var style = _styles[count];
                    foreach (var prop in _style_types)
                    {
                        var val = prop.GetValue(style, null);
                        if (val != null)
                        {
                            var my = t.GetField("_"+prop.Name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            if (my != null)
                                my.SetValue(this, val);
                        }
                    }
                }
            }

            private void ResolveFrom(int start)
            {
                var clone = (Resolver)MemberwiseClone();

                Resolve(start);

                foreach (var field in _types)
                {
                   var org_val = field.GetValue(clone);
                   var new_val = field.GetValue(this);
                   if (org_val != new_val && (org_val == null || !org_val.Equals(new_val)))
                   {
                       var atr = (IncludePropertyAttribute) Attribute.GetCustomAttribute(field, typeof(IncludePropertyAttribute));
                       FireStyleChanged(atr.StyleType, atr.PropertyName);
                   }
                }
            }

            class IncludePropertyAttribute : Attribute
            {
                public readonly ChangeType StyleType;
                public readonly string PropertyName;

                public IncludePropertyAttribute(ChangeType t, string name)
                {
                    StyleType = t;
                    PropertyName = name;
                }
            }

            #endregion

            #region Helper functions

            internal bool BorderUniform
            {
                get
                {
                    return (_BorderLeft == _BorderRight && _BorderLeft == _BorderTop && _BorderLeft == _BorderBottom)
                        && cColor.Equals(BorderColorLeft, BorderColorRight)
                        && cColor.Equals(BorderColorLeft, BorderColorTop)
                        && cColor.Equals(BorderColorLeft, BorderColorBottom)
                        && xDashStyle.Equals(DashLeft, DashRight)
                        && xDashStyle.Equals(DashLeft, DashTop)
                        && xDashStyle.Equals(DashLeft, DashBottom);

                }
            }
            internal bool HasDash { get { return DashBottom != null || DashLeft != null || DashRight != null || DashTop != null; } }

            internal bool HasAutoSize
            {
                get { return (_Width == null ? Display != eDisplay.Block : _Width.Value.Unit == cUnit.Auto) || Height.Unit == cUnit.Auto; }
            }
            internal bool HasPrecentSize
            {
                get
                {
                    if (_Width == null)
                        return Display == eDisplay.Block || _Height.Unit == cUnit.Precentage && _Height.Value != 0;
                    return _Width.Value.Unit == cUnit.Precentage && _Width.Value.Value != 0 ||
                           _Height.Unit == cUnit.Precentage && _Height.Value != 0;
                }
            }
            internal bool HasPrecentPosition
            {
                get
                {
                    if (Position == ePosition.Static)
                        return false;
                    return Top.Unit == cUnit.Precentage || Left.Unit == cUnit.Precentage ||
                        Right.Unit == cUnit.Precentage || Bottom.Unit == cUnit.Precentage;
                }
            }

            internal bool HasBorderRadius(double min_prec)
            {
                return !Util.Real.Same(_BorderRadiusLL.Value, 0, min_prec) ||
                        !Util.Real.Same(_BorderRadiusLR.Value, 0, min_prec) ||
                        !Util.Real.Same(_BorderRadiusUL.Value, 0, min_prec) ||
                        !Util.Real.Same(_BorderRadiusUR.Value, 0, min_prec);
            }
            internal bool HasBorder
            {
                //Should not use !Util.Real.Same( ... , 0, _min_prec) here as these properties
                //are used blindly. Should perhaps round them before use.
                get { return _BorderLeft.Value != 0 || _BorderRight.Value != 0 || _BorderTop.Value != 0 || _BorderBottom.Value != 0; }
            }
            internal bool HasBorderPrecent
            {
                get
                {
                    return _BorderLeft.Value != 0 && _BorderLeft.Unit == cUnit.Precentage ||
                           _BorderRight.Value != 0 && _BorderRight.Unit == cUnit.Precentage ||
                           _BorderTop.Value != 0 && _BorderTop.Unit == cUnit.Precentage ||
                           _BorderBottom.Value != 0 && _BorderBottom.Unit == cUnit.Precentage;
                }
            }
            internal bool HasMargin
            {
                get { return _MarginLeft.Value != 0 || _MarginRight.Value != 0 || _MarginTop.Value != 0 || _MarginBottom.Value != 0; }
            }
            internal bool HasMarginPrecent
            {
                get { return _MarginLeft.Value != 0 && _MarginLeft.Unit == cUnit.Precentage || 
                             _MarginRight.Value != 0 && _MarginRight.Unit == cUnit.Precentage || 
                             _MarginTop.Value != 0 && _MarginTop.Unit == cUnit.Precentage || 
                             _MarginBottom.Value != 0 && _MarginBottom.Unit == cUnit.Precentage; }
            }

            #endregion

            public cStyle this[int index]
            {
                get { return _styles[index]; }
            }
        }
    }
}
