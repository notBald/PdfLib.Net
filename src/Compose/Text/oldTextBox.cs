#define OVERDRAW //Fixes thin white lines using overdraw, though it might be better to align the points to pixel bounderies.
#define QUADRACTIC_CURVES //Seems to work/look better than the semi-cubic
#define DIRECT_DRAW //Draws directly to the target, instead of building a list of commands to execute. If a list is desired, use DrawCMD as the target.
#define MUL_PAR_GAP_WITH_LH //Multiplies the paragraph gap with line height. Seems to be more intuative.

//Current implementation always spits out a Td command before it starts to draw the text. One can avoid that, but this
//implementation is ugly as the public "RenderText" function can suddenly behave differently, which isn't acceptable.
//#define DONT_MOVE_OPTIMIZATION

using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using PdfLib.Pdf.ColorSpace;

namespace PdfLib.Compose.Text
{
    /// <summary>
    /// A square textbox that can render a document.
    /// 
    /// Obsolete, use chTextBox instead. The reason I'm keeping this around is because I got code that works, which
    /// depends on the idiosyncrasies of this code. Other than that there's no benifits to this class. 
    /// </summary>
    /// <remarks>
    /// Roadmap:
    ///  1. Indent right/left + hang
    ///  
    ///     This should be the next feature. It's already half-implemented, and will be usefull for implementing the
    ///     next feature. 
    ///  2. Lists
    ///  
    ///     Faces some of the same challanges as Indent. Naturally one also need to draw the character marking the list.
    ///     
    ///  3. Add textboxes to lines (prepwork for step 5)
    ///  
    ///     It's already possible to add XObjects. This can be extended to objects that implements a certain interface.
    ///     These objects are delay rendered, so there shouldn't be any issues. The only requirement is that they
    ///     have a size.
    ///     
    ///  4. Extend the underline functionality to be more generic.
    ///  
    ///     There are two challanges here: Post and pre text rendering. Post is already implemented, and works, while
    ///     pre is a bit harder.
    ///     
    ///     Idea 1: Render lines as normal, but if a "adorner" block is encountered, wait to commit the text commands. 
    ///             In the current implementation, text isn't rendered until "MakeBlocks" is called on the text renderer.
    ///             
    ///     Idea 2: Save the text metrix when an adorner goes active. Then, when the adorner goes off, do a ET draw BT set TM
    ///             Remeber, text isn't commited until MakeBloks. This needs some thought, but is likely the most maintainable
    ///             approach.
    ///             
    ///             Also, drop support for non-direct draw. With DrawCMD, one can get the delay functionality without having
    ///             to recompile.
    ///             
    ///  5. Blocks that can span multiple lines.
    ///  
    ///     This might seem hard at first blush, but isn't really.
    ///     
    ///     There are two scenarios. Free floating, and anchored.
    ///     
    ///     1. free floating:
    ///     
    ///     These blocks are added to the document with a rectangular size. Lines are to be flowed around this rectangle, which
    ///     can require multiple layout passes. The problem is that the height of a line can change, depending on the block
    ///     pushing a word off a line, or not.
    ///     
    ///     I think something decent can be managed with one pass. One scenario that would look a bit odd, is when a line has
    ///     a large word at the end that gets pushed off because the heightned line intersects with the block. Pushing off this
    ///     word, then, puts it on the next line, and make the current one much lower. Resulting in text being broken between
    ///     the empty area above the block. But, this is a edge case. 
    ///     
    ///     Wait. My I forgot about text alignment. Hmm. Looking at word, I see that text aligment won't be that much of a problem.
    ///     Yeah. Word aligns each fragment as if it was the whole line.
    ///     
    ///     So yeah, single pass. The line that is returned must communicate how it is to be moved. I.e. it needs to be moved
    ///     down the full height of the line. Hmm. Or not. The line don't care about how it's rendered.
    ///     
    ///     Idea. First, drop HeightLayout(). It's only usefull for real time editing, which is not a use case for this textbox.
    ///           It's certainly possible to optimize "layout" in various ways, but there's not currently a need for this
    ///           
    ///           1. Measure the full line. This gives you the height, after line wrapping.
    ///           2. Test if the rectangle overlaps with any blocks. If it does, remeasure the line to fit within the spaces bewteen
    ///              these blocks*.
    ///               - Special case when a block is to the left or to the right of the line. Like Word, in that case, shift the
    ///                 line, indent and all, to the right/left of the block.
    ///              * Rember to query the block itself for overlap. That way support for non-rectangular regions can easily be 
    ///                slotted in later.
    ///           3. You now got a bunch of line fragments. Remeasure the line into these fragments. Word don't care about aligment
    ///              and neither shall we. However, tab positions must not change. So rember to use update textmatrix correctly.
    ///           4. Recalculate the height of each line fragment to be the tallest fragment
    ///           5. Check if this new rectagle overlaps the regions it previously overlapped. If it don't, recombine them into a
    ///              single line. There's no need to remeasure for keerning, as that will be done when rendering.
    ///           6. When rendering the line fragments, render them as if they were a normal line with two exception. 1) Right/Left
    ///              indent is only to be appplied to the rightmost/leftmost fragment. 2) SetOffset must be adjusted between each 
    ///              fragment for the sake of tabulators.
    ///              
    ///     2. Anchored
    ///     
    ///     These are blocks anchored to a line, somehow.
    ///     
    ///     Idea. Add the block to the line + the document, like normal, just with zero height. Since the block has zero height,
    ///           nothing will overlap with it. The line it's "on" will lay itself out around the block, like normal. No code changes
    ///           needed. However, it's at this point the block must say, "I got height!" So any subsequent line will be tested 
    ///           against it. 
    ///  
    ///  6. Horizontal divider.
    ///  7. Tables
    ///  
    ///     Idea. A table can be made out of text boxes. They can already size themselves and draw borders. Making this more of
    ///           a grid layout manager. Since tables are blocks to, they can then be put inside a textbox.
    /// </remarks>
    public class oldTextBox
    {
        #region Variables and properties

        #region Position

        /// <summary>
        /// If set, the textbox will have a position.
        /// </summary>
        public xPoint Position = new xPoint();

        /// <summary>
        /// Rotates the textbox
        /// </summary>
        public double RotationAngle = 0;

        /// <summary>
        /// Point to rotate the textbox around. By default, center point is used.
        /// For upper right, set (1,1), lower left, (0,0) etc.
        /// </summary>
        public xPoint RotationPoint = new xPoint(.5, .5);

        #endregion

        #region Appearance

        /// <summary>
        /// How the textbox handles text that overflows its boundaries.
        /// </summary>
        public Overdraw Overflow = Overdraw.Hidden;

        /// <summary>
        /// Whenever sidebearings is to be stripped
        /// </summary>
        private bool _strip_sb = true;

        /// <summary>
        /// Whenever or not to strip the side bearings from the text when
        /// calculating content width. Also note, when true, left and right aligned
        /// text is postitioned perfectly at the edge.
        /// </summary>
        public bool StripSideBearings
        {
            get { return _strip_sb; }
            set
            {
                if (value != _strip_sb)
                {
                    _strip_sb = value;

                    //A complete layout isn't actually needed, since only the content
                    //content width needs recalcing. That code can be added to "HeightLayout".
                    //(e.g. go through each line and call GetContentWidth(_strip_sb, rtl or ltr)
                    Layout();
                }
            }
        }

        /// <summary>
        /// Font used if no font is defined.
        /// </summary>
        public cFont DefaultFont
        {
            get { return _org_state.Tf; }
            set
            {
                if (!ReferenceEquals(value, _org_state.Tf))
                {
                    _org_state.Tf = value;
                    Layout();
                }
            }
        }

        /// <summary>
        /// Sets the font and redoes the textbox layout
        /// </summary>
        public cFont Font
        {
            get { return _org_state.Tf; }
            set
            {
                if (!ReferenceEquals(_org_state.Tf, value))
                {
                    _org_state.Tf = value;
                    Layout();
                }
            }
        }

        /// <summary>
        /// Standar size for fonts
        /// </summary>
        public double DefaultFontSize
        {
            get { return _org_state.Tfs; }
            set
            {
                if (value != _org_state.Tfs)
                {
                    _org_state.Tfs = value;
                    Layout();
                }
            }
        }

        /// <summary>
        /// Sets the fontsize and redoes the textbox layout
        /// </summary>
        public double FontSize
        {
            get { return _org_state.Tfs; }
            set
            {
                if (value != _org_state.Tfs)
                {
                    _org_state.Tfs = value;
                    Layout();
                }
            }
        }

        /// <summary>
        /// Color of non-outline fonts
        /// </summary>
        public cBrush DefaultFontColor
        {
            get { return _org_state.FillColor; }
            set { _org_state.FillColor = value; }
        }

        /// <summary>
        /// Color of outlined text
        /// </summary>
        public cBrush DefaultOutlineColor
        {
            get { return _org_state.StrokeColor; }
            set { _org_state.StrokeColor = value; }
        }

        /// <summary>
        /// Thickness of outlines
        /// </summary>
        public double DefaultOutlineWidth
        {
            get { return _org_state.StrokeWidth; }
            set { _org_state.StrokeWidth = value; }
        }


        /// <summary>
        /// Character used for breaking words. Use '\0' for no character
        /// </summary>
        public char BreakWordCharacter
        {
            get { return _org_state.BreakWordCharacter != null ? _org_state.BreakWordCharacter.Value : '\0'; }
            set
            {
                if (value == '\0')
                    _org_state.BreakWordCharacter = null;
                else
                    _org_state.BreakWordCharacter = value;
                if (_org_state.BreakWord)
                    Layout();
            }
        }

        /// <summary>
        /// Sets right to left text flow.
        /// </summary>
        public bool RightToLeft
        {
            get { return _org_state.RightToLeft; }
            set { _org_state.RightToLeft = value; }
        }

        /// <summary>
        /// How much to overdraw border curves to prevent white lines
        /// </summary>
        public double Overdraw_curve = 0.003;

        /// <summary>
        /// How much to overdraw border corners to prevent white lines
        /// </summary>
        public double Overdraw_corner = 0.2;

        /// <summary>
        /// Note, it's currently up to the client to compensate the draw position for these.
        /// </summary>
        public double MarginLeft, MarginRight, MarginTop, MarginBottom;

        public double PaddingLeft, PaddingRight, PaddingTop, PaddingBottom;
        public double? Padding
        {
            get
            {
                if (PaddingLeft == PaddingRight && PaddingLeft == PaddingTop && PaddingLeft == PaddingBottom)
                    return PaddingLeft;
                return double.NaN;
            }
            set
            {
                double val = value == null ? 0 : value.Value;
                PaddingLeft = val;
                PaddingRight = val;
                PaddingTop = val;
                PaddingBottom = val;
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

        private double _RadiusLL, _RadiusLR, _RadiusUL, _RadiusUR;
        /// <summary>
        /// Border Radius Lower Left corner
        /// </summary>
        public double BorderRadiusLL
        {
            get { return _RadiusLL; }
            set { _RadiusLL = Math.Max(value, 0); }
        }
        /// <summary>
        /// Border Radius Lower Right corner
        /// </summary>
        public double BorderRadiusLR
        {
            get { return _RadiusLR; }
            set { _RadiusLR = Math.Max(value, 0); }
        }
        /// <summary>
        /// Border Radius Upper Left corner
        /// </summary>
        public double BorderRadiusUL
        {
            get { return _RadiusUL; }
            set { _RadiusUL = Math.Max(value, 0); }
        }
        /// <summary>
        /// Border Radius Upper Right corner
        /// </summary>
        public double BorderRadiusUR
        {
            get { return _RadiusUR; }
            set { _RadiusUR = Math.Max(value, 0); }
        }
        public double? BorderRadius
        {
            get
            {
                if (_RadiusLL == _RadiusLR && _RadiusLL == _RadiusUL && _RadiusLL == _RadiusUR)
                    return _RadiusLL;
                return double.NaN;
            }
            set
            {
                double val = Math.Max(value == null ? 0 : value.Value, 0);
                _RadiusLL = val;
                _RadiusLR = val;
                _RadiusUL = val;
                _RadiusUR = val;
            }
        }
        private bool HasBorderRadius
        {
            get
            {
                return !Util.Real.Same(_RadiusLL, 0, _min_prec) ||
                       !Util.Real.Same(_RadiusLR, 0, _min_prec) ||
                       !Util.Real.Same(_RadiusUL, 0, _min_prec) ||
                       !Util.Real.Same(_RadiusUR, 0, _min_prec);
            }
        }
        private bool HasBorder
        {
            //Should not use !Util.Real.Same( ... , 0, _min_prec) here as these properties
            //are used blindly. Should perhaps round them before use.
            get { return _BLeft != 0 || _BRight != 0 || _BTop != 0 || _BBottom != 0; }
        }
        private double _BLeft, _BRight, _BTop, _BBottom;
        /// <summary>
        /// Left border width
        /// </summary>
        public double BorderLeft
        {
            get { return _BLeft; }
            set { _BLeft = Math.Max(value, 0); }
        }
        /// <summary>
        /// Right border width
        /// </summary>
        public double BorderRight
        {
            get { return _BRight; }
            set { _BRight = Math.Max(value, 0); }
        }
        /// <summary>
        /// Top border width
        /// </summary>
        public double BorderTop
        {
            get { return _BTop; }
            set { _BTop = Math.Max(value, 0); }
        }
        /// <summary>
        /// Bottom border width
        /// </summary>
        public double BorderBottom
        {
            get { return _BBottom; }
            set { _BBottom = Math.Max(value, 0); }
        }
        public double? BorderTichness
        {
            get
            {
                if (_BLeft == _BRight && _BLeft == _BTop && _BLeft == _BBottom)
                    return _BLeft;
                return double.NaN;
            }
            set
            {
                double val = value == null ? 0 : value.Value;
                _BLeft = val;
                _BRight = val;
                _BTop = val;
                _BBottom = val;
            }
        }
        public xDashStyle? DashLeft, DashRight, DashTop, DashBottom;
        private bool HasDash { get { return DashBottom != null || DashLeft != null || DashRight != null || DashTop != null; } }
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
                DashLeft = value;
                DashRight = value;
                DashTop = value;
                DashBottom = value;
            }
        }
        public cColor BorderColorLeft, BorderColorRight, BorderColorTop, BorderColorBottom;
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
                BorderColorLeft = value;
                BorderColorRight = value;
                BorderColorTop = value;
                BorderColorBottom = value;
            }
        }

        private bool BoderUniform
        {
            get
            {
                return (_BLeft == _BRight && _BLeft == _BTop && _BLeft == _BBottom)
                    && cColor.Equals(BorderColorLeft, BorderColorRight)
                    && cColor.Equals(BorderColorLeft, BorderColorTop)
                    && cColor.Equals(BorderColorLeft, BorderColorBottom)
                    && xDashStyle.Equals(DashLeft, DashRight)
                    && xDashStyle.Equals(DashLeft, DashTop)
                    && xDashStyle.Equals(DashLeft, DashBottom);

            }
        }

        public cBrush BackgroundColor, ForgroundColor;

        /// <summary>
        /// If true, the text box will position itself over the point
        /// it was drawn instead of under it
        /// </summary>
        public bool PositionOver = false;

        #endregion

        #region Document

        private chDocument _doc;
        private chParagraph.ParMetrics[] _par_metrics;
        private readonly chDocument.ParagraphChangedHandler _change_handler;
        private xIntRange _range = new xIntRange(0, int.MaxValue);

        private int _line_count = 0;

        private int _last_displayed_character = -1;

        public int LastDisplayedCharacter { get { return _last_displayed_character; } }

        /// <summary>
        /// Number of lines handled by the text box. If Overflow = hidden and height is set, then
        /// this will be the number of lines displayed.
        /// </summary>
        public int LineCount { get { return _line_count; } }

        /// <summary>
        /// First and last character to show from the document.
        /// </summary>
        public xIntRange DocumentRange
        {
            get { return _range; }
            set
            {
                if (value != _range)
                {
                    _range = value;
                    Layout();
                }
            }
        }

        public chDocument Document
        {
            get { return _doc; }
            set
            {
                if (_doc != null)
                    _doc.OnParagraphChanged -= _change_handler;
                if (value != null)
                    value.OnParagraphChanged += _change_handler;
                SetDocument(value);
            }
        }
        internal void SetDocument(chDocument doc)
        {
            _doc = doc;
            if (_doc != null)
            {
                _par_metrics = new chParagraph.ParMetrics[_doc.ParagraphCount];
                int count = 0;
                foreach (var par in _doc)
                    _par_metrics[count++] = par.CreateMetrics();
                Layout();
            }
        }

        #endregion

        #region Layout

        private double _paragraph_gap = 1;

        /// <summary>
        /// Distance between paragraphs. The distance is multiplied
        /// with the lineheight of first line in the next paragraph.
        /// </summary>
        public double ParagraphGap
        {
            get { return _paragraph_gap; }
            set
            {
                if (value != _paragraph_gap)
                {
                    _paragraph_gap = value;
                    HeightLayout();
                }
            }
        }

        /// <summary>
        /// Standar width between lines.
        /// </summary>
        private double _def_line_height = 1;

        public double DefaultLineHeight
        {
            get { return _def_line_height; }
            set
            {
                if (value != _def_line_height)
                {
                    _def_line_height = value;
                    HeightLayout();
                }
            }
        }

        #endregion

        #region State

        private cRenderState _org_state;

        private double _min_prec;

        #endregion

        #region Textbox size

        private double _content_width;
        /// <summary>
        /// If the textbox has a fixed content width
        /// </summary>
        private bool has_width;
        private double? _height;

        /// <summary>
        /// Width of the textbox
        /// </summary>
        public double Width
        {
            get { return _content_width + PaddingLeft + PaddingRight + MarginLeft + MarginRight + _BLeft + _BRight; }
            set
            {
                ContentWidth = value - (PaddingRight + PaddingLeft + MarginLeft + MarginRight + _BLeft + _BRight);
            }
        }

        /// <summary>
        /// Width of the textbox, including the border and the padding
        /// </summary>
        public double BorderWidth
        {
            get { return _content_width + PaddingLeft + PaddingRight + _BLeft + _BRight; }
            set
            {
                ContentWidth = value - (PaddingRight + PaddingLeft + _BLeft + _BRight);
            }
        }

        /// <summary>
        /// Width of the aread inide the border of the textbox
        /// </summary>
        public double InnerWidth
        {
            get { return _content_width + PaddingLeft + PaddingRight; }
            set
            {
                ContentWidth = value - PaddingLeft - PaddingRight;
            }
        }

        /// <summary>
        /// Width of the content of the text box
        /// </summary>
        public double ContentWidth
        {
            get { return _content_width; }
            set
            {
                _content_width = value;
                has_width = true;
                Layout();
            }
        }


        public double Height
        {
            get { return ContentHeight + PaddingTop + PaddingBottom + MarginTop + MarginBottom + _BTop + _BBottom; }
            set { ContentHeight = value - (PaddingTop + PaddingBottom + MarginTop + MarginBottom + _BTop + _BBottom); }
        }

        public double InnerHeight
        {
            get { return ContentHeight + PaddingTop + PaddingBottom; }
            set { ContentHeight = value - (PaddingTop + PaddingBottom); }
        }

        /// <summary>
        /// Height of the textbox's content, set null to have the textbox automatically size itself
        /// </summary>
        public double ContentHeight
        {
            set { _height = value; }
            get
            {
                if (_height != null)
                    return _height.Value;
                if (_par_metrics.Length == 0)
                    return 0;
                double height = _par_metrics[0].Height;
                for (int c = 1; c < _par_metrics.Length; c++)
                {
                    height += _par_metrics[c].Height;
                    height += _par_metrics[c].ParGap * _paragraph_gap;
                }
                height -= _par_metrics[_par_metrics.Length - 1].LastDescent;
                return height;
            }
        }

        private double GetHeight(int paragraph_count)
        {
            double height = _par_metrics[0].Height;
            for (int c = 1; c < paragraph_count; c++)
            {
                height += _par_metrics[c].Height;
                height += _par_metrics[c].ParGap * _paragraph_gap;
            }
            return height;
        }

        private int GetParStartPos(int index)
        {
            int schar = 0, c;
            for (c = 0; c < index; c++)
            {
                schar += _par_metrics[index].CharCount;
            }
            return schar + c;
        }

        #endregion

        #endregion

        #region Init

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="content_width">Width of the text box's content. Padding comes in addition to this.</param>
        /// <param name="content_height">Height of the text box's content. Padding comes in addition to this.</param>
        public oldTextBox(double content_width, double? content_height)
            : this(null, content_width, content_height, new cRenderState(cFont.Create("Helvetica")))
        { }

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="doc">Document to display</param>
        /// <param name="initial_state">
        /// Various parameters relating to how the text is to be displayed. The text
        /// box will use these parameters to lay out the text. When calling the normal
        /// Render function, the supplied state will be adjusted to conform to the
        /// text box's initial state.
        /// </param>
        public oldTextBox(chDocument doc, cRenderState initial_state)
            : this(doc, null, initial_state)
        { }

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="doc">Text to display</param>
        /// <param name="font">Default font</param>
        /// <param name="font_size">Default font size</param>
        public oldTextBox(string doc, cFont font, double font_size)
            : this(new chDocument(doc), null, new cRenderState(font, font_size))
        { }

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="doc">Text to display</param>
        /// <param name="content_width">Width of the text box's content. Padding comes in addition to this.</param>
        public oldTextBox(string doc, double content_width)
            : this(new chDocument(doc), content_width)
        { }

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="doc">Text to display</param>
        /// <param name="initial_state">
        /// Various parameters relating to how the text is to be displayed. The text
        /// box will use these parameters to lay out the text. When calling the normal
        /// Render function, the supplied state will be adjusted to conform to the
        /// text box's initial state.
        /// </param>
        internal oldTextBox(string doc, cRenderState initial_state)
            : this(new chDocument(doc), null, initial_state)
        { }

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="doc">Document to display</param>
        /// <param name="content_width">Width of the text box's content. Padding comes in addition to this.</param>
        public oldTextBox(chDocument doc, double content_width)
            : this(doc, content_width, new cRenderState(cFont.Create("Helvetica")))
        { }

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="doc">Document to display</param>
        /// <param name="content_width">Width of the text box's content. Padding comes in addition to this.</param>
        /// <param name="initial_state">
        /// Various parameters relating to how the text is to be displayed. The text
        /// box will use these parameters to lay out the text. When calling the normal
        /// Render function, the supplied state will be adjusted to conform to the
        /// text box's initial state.
        /// </param>
        public oldTextBox(chDocument doc, double? content_width, cRenderState initial_state)
            : this(doc, content_width, null, initial_state)
        { }

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="doc">Document to display</param>
        /// <param name="content_width">Width of the text box's content. Padding comes in addition to this.</param>
        /// <param name="content_height">Height of the text box's content. Padding comes in addition to this.</param>
        /// <param name="initial_state">
        /// Various parameters relating to how the text is to be displayed. The text
        /// box will use these parameters to lay out the text. When calling the normal
        /// Render function, the supplied state will be adjusted to conform to the
        /// text box's initial state.
        /// </param>
        public oldTextBox(chDocument doc, double? content_width, double? content_height, cRenderState initial_state)
        {
            _change_handler = new chDocument.ParagraphChangedHandler(_doc_OnParagraphChanged);
            _org_state = initial_state.Copy();
            _org_state.BreakWordCharacter = '-';
            if (content_width != null)
            {
                _content_width = content_width.Value;
                has_width = true;
            }
            _height = content_height;
            Document = doc;
        }

        void _doc_OnParagraphChanged(chParagraph par, int index, bool added, bool layout)
        {
            if (added)
            {
                throw new NotImplementedException();
            }

            if (layout)
            {
                var cur_state = _org_state.Copy();
                int start = GetParStartPos(index);
                xIntRange par_range = new xIntRange(start, start + par.Length);
                if (par_range.Overlaps(_range))
                    Layout(par, _par_metrics[index], cur_state, GetHeight(index), index == 0 ? 0 : _paragraph_gap, Math.Max(par_range.Min, _range.Min) - par_range.Min, Math.Min(par_range.Max, _range.Max) - par_range.Min);
            }
        }

        #endregion

        #region Layout

        /// <summary>
        /// For when only the height has changed
        /// </summary>
        private void HeightLayout()
        {
            if (_height.HasValue && Overflow == Overdraw.Hidden)
            {
                //Heightlayout hasn't been updated to handle documents
                //where there may be lines that has yet to be layed out.
                //
                //So we call for a full layout.
                Layout();
                return;
            }

            if (_doc == null) return;

            int c = 0;
            xIntRange char_range = new xIntRange();
            foreach (var par in _par_metrics)
            {
                char_range.Max = char_range.Min + par.CharCount;

                if (char_range.Overlaps(_range))
                    HeightLayout(par, c++);
                else if (_range.Max < char_range.Min)
                    break;

                char_range.Min = char_range.Max + 1;
            }
        }

        /// <summary>
        /// For when only the height has changed
        /// </summary>
        private void HeightLayout(chParagraph.ParMetrics par, int index)
        {
            int line_count = _par_metrics[index].LineCount;
            double height, full_height, lh = par.Par.LineHeight == null ? _def_line_height : par.Par.LineHeight.Value;
            chLineWrapper.LineVMetrics line, prev = null;

            line = par.GetVMetrics(0);
            if (line == null)
            {
                _par_metrics[index].ParGap = 0;
                _par_metrics[index].Height = 0;
                return;
            }
            full_height = line.Ascent; //Don't mul first line with lh
            line.Height = full_height;

            //Sets the paragraph gap to the height of the first line.
#if MUL_PAR_GAP_WITH_LH
            _par_metrics[index].ParGap = line.FontHeight * lh;
#else
            _par_metrics[index].ParGap = line.FontHeight
#endif
            prev = line;

            for (int count = 1; count < line_count; count++)
            {
                line = par.GetVMetrics(count);
                if (line == null)
                    break;

                height = line.FontHeight * lh;
                //if (!par.Par.ForceLineHeight)
                //{
                //    var space = height - line.Ascent + Math.Min(0, prev.YMin);
                //    if (space < 0)
                //        height -= space;
                //}
                line.Height = height;
                full_height += height;
                prev = line;
            }
            _par_metrics[index].Height = full_height;
            _par_metrics[index].LastDescent = prev.Descent;
        }

        private void Layout()
        {
            if (_doc == null) return;

            var cur_state = _org_state.Copy();
            if (cur_state.Tf == null)
                throw new NotSupportedException("Default font required");

            //If the width isn't set, it needs to be calculated
            if (!has_width)
                _content_width = 0;

            double full_height = 0;
            xIntRange par_range = new xIntRange();
            bool first = true;
            _last_displayed_character = -1;
            for (int index = 0; index < _doc.ParagraphCount; index++)
            {
                var par = _doc[index];
                var par_length = par.Length;
                par_range.Max = par_range.Min + par_length;
                _par_metrics[index].CharCount = par_length;

                if (par_range.Overlaps(_range))
                {
                    var pm = _par_metrics[index];
                    int end = Math.Min(_range.Max, par_range.Max) - par_range.Min;
                    int layout_end = Layout(
                        _doc[index],
                        pm, cur_state,
                        full_height,
                        first ? 0 : _paragraph_gap,
                        Math.Max(_range.Min, par_range.Min) - par_range.Min,
                        end
                        );
                    full_height += pm.Height;
                    if (end != layout_end)
                    {
                        _last_displayed_character = par_range.Min + layout_end;
                        break;
                    }

                    first = false;
                }
                else if (_range.Max < par_range.Min)
                    break;

                par_range.Min = par_range.Max + 1;
            }
        }

        /// <summary>
        /// Lays out a paragraph
        /// </summary>
        /// <param name="par">Paragraph to lay out</param>
        /// <param name="par_metrics">Metrics for this paragraph will be stored in this object</param>
        /// <param name="current_height">Current height of the text</param>
        /// <param name="paragraph_gap">Gap before the paragraph is multiplied with this value. Set 0 for no gap</param>
        /// <param name="start_cr">First character in the document</param>
        /// <param name="end_chr">Last character in the document</param>
        /// <returns>Last character layed out</returns>
        private int Layout(chParagraph par, chParagraph.ParMetrics par_metrics, cRenderState cur_state, double current_height, double paragraph_gap, int start_cr, int end_chr)
        {
            //Width of this paragraph's content
            var content_width = has_width ? _content_width : double.MaxValue;

            //Number of lines in this paragraph
            _line_count -= par_metrics.LineCount;

            //Resets layout data for this paragraph
            par_metrics.ResetLayout();
            double height, full_height = 0, lh = par.LineHeight == null ? _def_line_height : par.LineHeight.Value;
            chLineWrapper.LineVMetrics line, prev = null;
            line = par_metrics.SetLineWidth(content_width, _def_line_height, cur_state, start_cr, end_chr);

            //Null means there's nothing in the paragraph. Not even a line. If such a paragraph pops up,
            //it will be completly ignored.
            if (line == null)
            {
                par_metrics.ParGap = 0;
                par_metrics.Height = 0;
                par_metrics.LineCount = 0;

                return start_cr - 1;
            }
            int count = 1; //<-- counts nr. lines

            //When rendering a paragraph, one start by moving down the ascent of the first line,
            //not the full line height. Also note that lh is a multiplier for height between
            //paragraph lines, not paragraps, so line.Ascent is not to be multiplied by it.
            // CapHeight is prefered over Ascent (When cap height is unknown, ascent is returned)
            full_height = line.CapHeight;
            if (line.YMax > full_height)
                full_height = line.YMax;
            line.Height = full_height;

            //This is the gap up to the previous paragraph
#if MUL_PAR_GAP_WITH_LH
            par_metrics.ParGap = line.FontHeight * lh;
#else
            par_metrics.ParGap = line.FontHeight;
#endif
            full_height += par_metrics.ParGap * paragraph_gap;

            //There may not be room for the first line of the paragraph. 
            if (Overflow == Overdraw.Hidden && _height != null && current_height + full_height > _height.Value)
            {
                par_metrics.ParGap = 0;
                par_metrics.Height = 0;
                par_metrics.LineCount = 0;
                par_metrics.CharCount = 0;

                return start_cr - 1;
            }

            //Line is adjusted down with FirstLineIndent, so we have to add that value
            double max_width = line.GetContentWidth(_strip_sb, cur_state.RightToLeft) + (start_cr == 0 ? par.FirstLineIndent : 0);
            start_cr += line.CharCount;
            prev = line;

            while (start_cr <= end_chr)
            {
                line = par_metrics.SetLineWidth(content_width, _def_line_height, cur_state, start_cr, end_chr);
                if (line == null)
                {
                    end_chr = start_cr - 1;
                    break;
                }

                max_width = Math.Max(line.GetContentWidth(_strip_sb, cur_state.RightToLeft), max_width);
                height = line.FontHeight * lh;
                //if (!par.ForceLineHeight)
                //{
                //    var space = height - line.Ascent + Math.Min(0, prev.YMin);
                //    if (space < 0)
                //        height -= space;
                //}
                line.Height = height;
                full_height += height;

                if (Overflow == Overdraw.Hidden && _height != null && current_height + full_height > _height.Value)
                {
                    full_height -= height;
                    end_chr = start_cr - 1;
                    break;
                }

                start_cr += line.CharCount; //<-- Will be +1 too big when reaching end_chr (unless end_chr is the true end of the line).
                prev = line;
                count++;
            }
            par_metrics.Height = full_height;
            par_metrics.LineCount = count;
            par_metrics.LastDescent = prev.Descent;
            _line_count += count;

            if (!has_width)
                _content_width = Math.Max(_content_width, max_width);

            return end_chr;
        }

        #endregion

        #region Render

        /// <summary>
        /// Creates the 8 points in a curved border
        /// </summary>
        /// <remarks>
        ///       -P1---------P2
        ///      /              \
        ///     P0              P3
        ///     |               |
        ///     P7              P4
        ///      \              /
        ///       -P6--------P5-
        /// </remarks>
        private xPoint[] CreatePoints(double x, double y, double width, double height)
        {
            Debug.Assert(width >= 0 && height >= 0);
            var pts = new xPoint[8];
            //Limiting border radius to half width/height. One could allow for more but
            //one would then have to check if it overlaps the other side.
            double w2 = width / 2, h2 = height / 2;
            pts[0] = new xPoint(x, y + height - Math.Min(h2, _RadiusUL));
            pts[1] = new xPoint(x + Math.Min(w2, _RadiusUL), y + height);
            pts[2] = new xPoint(x + width - Math.Min(w2, _RadiusUR), y + height);
            pts[3] = new xPoint(x + width, y + height - Math.Min(h2, _RadiusUR));
            pts[4] = new xPoint(x + width, y + Math.Min(h2, _RadiusLR));
            pts[5] = new xPoint(x + width - Math.Min(w2, _RadiusLR), y);
            pts[6] = new xPoint(x + Math.Min(w2, _RadiusLL), y);
            pts[7] = new xPoint(x, y + Math.Min(h2, _RadiusLL));
            return pts;
        }

        /// <summary>
        /// When stroking, the clip outline needs to have
        /// it's coorindates adjusted so that the curves
        /// align. 
        /// </summary>
        private xPoint[] Adjust(xPoint[] stroke, xPoint[] outline)
        {
            var pts = new xPoint[8];
            if (Util.Real.Same(_RadiusUL, 0, _min_prec))
            {
                pts[0] = outline[0];
                pts[1] = outline[0];
            }
            else
            {
                pts[0] = new xPoint(outline[0].X, stroke[0].Y);
                pts[1] = new xPoint(stroke[1].X, outline[1].Y);
            }
            if (Util.Real.Same(_RadiusUR, 0, _min_prec))
            {
                pts[2] = outline[2];
                pts[3] = outline[2];
            }
            else
            {
                pts[2] = new xPoint(stroke[2].X, outline[2].Y);
                pts[3] = new xPoint(outline[3].X, stroke[3].Y);
            }
            if (Util.Real.Same(_RadiusLR, 0, _min_prec))
            {
                pts[4] = outline[4];
                pts[5] = outline[4];
            }
            else
            {
                pts[4] = new xPoint(outline[4].X, stroke[4].Y);
                pts[5] = new xPoint(stroke[5].X, outline[5].Y);
            }
            if (Util.Real.Same(_RadiusLL, 0, _min_prec))
            {
                pts[6] = outline[6];
                pts[7] = outline[6];
            }
            else
            {
                pts[6] = new xPoint(stroke[6].X, outline[6].Y);
                pts[7] = new xPoint(outline[7].X, stroke[7].Y);
            }
            return pts;
        }

        internal static void DrawRoundRect(xPoint[] pts, IDraw draw)
        {
            draw.MoveTo(pts[0].X, pts[0].Y);
            if (pts[0] != pts[1])
#if QUADRACTIC_CURVES
                cDraw.CurveTo(draw, pts[0], pts[0].X, pts[1].Y, pts[1].X, pts[1].Y);
#else
                draw.CurveTo(pts[0].X, pts[1].Y, pts[1].X, pts[1].Y);
#endif
            draw.LineTo(pts[2].X, pts[2].Y);
            if (pts[2] != pts[3])
#if QUADRACTIC_CURVES
                cDraw.CurveTo(draw, pts[2], pts[3].X, pts[2].Y, pts[3].X, pts[3].Y);
#else
                draw.CurveTo(pts[3].X, pts[2].Y, pts[3].X, pts[3].Y);
#endif
            draw.LineTo(pts[4].X, pts[4].Y);
            if (pts[4] != pts[5])
#if QUADRACTIC_CURVES
                cDraw.CurveTo(draw, pts[4], pts[4].X, pts[5].Y, pts[5].X, pts[5].Y);
#else
                draw.CurveTo(pts[4].X, pts[5].Y, pts[5].X, pts[5].Y);
#endif
            draw.LineTo(pts[6].X, pts[6].Y);
            if (pts[6] != pts[7])
#if QUADRACTIC_CURVES
                cDraw.CurveTo(draw, pts[6], pts[7].X, pts[6].Y, pts[7].X, pts[7].Y);
#else
                draw.CurveTo(pts[7].X, pts[6].Y, pts[7].X, pts[7].Y);
#endif
        }

#if QUADRACTIC_CURVES
        /// <summary>
        /// Converts a quardatic curve to a cubic curve
        /// </summary>
        /// <param name="start">Start point</param>
        /// <param name="ctrl1">Control point, and control point 1 of the cubic curve</param>
        /// <param name="end_ctrl2">End point and control point 2 of the cubic curve</param>
        private void QuadraticToCubic(xPoint start, ref xPoint ctrl1, ref xPoint end_ctrl2)
        {
            double x3 = end_ctrl2.X, y3 = end_ctrl2.Y;

            end_ctrl2.X = ctrl1.X + (end_ctrl2.X - ctrl1.X) / 3;
            end_ctrl2.Y = ctrl1.Y + (end_ctrl2.Y - ctrl1.Y) / 3;
            ctrl1.X = start.X + 2 * (ctrl1.X - start.X) / 3;
            ctrl1.Y = start.Y + 2 * (ctrl1.Y - start.Y) / 3;
        }
#endif

        /// <summary>
        /// Renders the text box. State will be saved and restored.
        /// </summary>
        /// <param name="draw">Render target</param>
        /// <param name="state">Render state</param>
        public void Render(IDraw draw, ref cRenderState state)
        {
            if (state.GS != GS.Page)
                throw new NotSupportedException("State must be page");

            _min_prec = 1 / Math.Pow(10, draw.Precision);
            draw.Save();
            state.Save();
            state.Set(_org_state, draw);
            double height = ContentHeight,
                   inner_height = height + PaddingTop + PaddingBottom,
                   border_height = inner_height + _BBottom + _BTop,
                //Todo: margin has to be tested out, with an eye towards rotation and such. Perhaps use border_height instead when rotating.
                //      This because it's the clients job to handle margins. It's perhaps best if the renderer don't touch margins at all.
                   outer_height = border_height + MarginBottom + MarginTop;
            xPoint[] clip = null, stroke = null;

#if DONT_MOVE_OPTIMIZATION
            //To avoid a needless Td, we special case this particular condition.
            if (PositionOver && !HasBorder && BackgroundColor == null)
                outer_height -= _doc[0].GetLine(0).LineHeight;
#endif

            if (RotationAngle != 0)
            {
                //Width:
                //double width = _content_width,
                //        inner_width = width + PaddingLeft + PaddingRight,
                //        border_width = inner_width + _BLeft + _BRight,
                //        outer_width = border_width + MarginLeft + MarginRight;

                //Rotate the textbox around its center point
                var rm = xMatrix.Identity.Rotate(RotationAngle, Width * RotationPoint.X, -outer_height * (1 - RotationPoint.Y));

                //Moves the textbox after rotation. 
                rm = rm.Translate(Position.X, Position.Y + (PositionOver ? outer_height : 0));

                draw.PrependCM(rm);
            }
            else if (Position.X != 0 || Position.Y != 0)
            {
                draw.PrependCM(new xMatrix(Position.X, Position.Y + (PositionOver ? outer_height : 0)));
            }
            else if (PositionOver)
            {
                //Moves the textbox over the drawing point
                draw.PrependCM(new xMatrix(0, outer_height));
            }

            //Sets clip path for curved borders
            if (HasBorderRadius && HasBorder)
            {
                draw.Save();
                state.Save();

                //Creates the stroke geometry
                stroke = CreatePoints(
                    _BLeft / 2,
                    -_BTop - inner_height - _BBottom / 2,
                    InnerWidth + _BLeft / 2 + _BRight / 2,
                    inner_height + _BBottom / 2 + _BTop / 2);

                //Creates the inner geometry.
                clip = CreatePoints(_BLeft, -_BTop - inner_height, InnerWidth, inner_height);

                //Adjusts it to the stroke geometry
                clip = Adjust(stroke, clip);

                DrawRoundRect(clip, draw);
                draw.ClosePath();
                draw.SetClip(xFillRule.Nonzero);
                draw.DrawPathNZ(false, false, false);
            }

            //Renders the background
            if (BackgroundColor != null)
            {
                var fc = state.fill_color;
                BackgroundColor.SetColor(state, true, draw);

                draw.RectAt(_BLeft, -_BTop, InnerWidth, -inner_height);
                draw.DrawPathNZ(false, false, true);

                fc.SetColor(state, true, draw);
            }

            //Draws the text
            double move_x = _BLeft + PaddingLeft,
                   move_y = _BTop + PaddingTop;
            if (Overflow == Overdraw.Clipped && _height != null)
            {
                if (!HasBorderRadius)
                {
                    draw.Save();
                    state.Save();
                }
                cDraw.Clip(draw, new xRect(move_x, -border_height + _BBottom + PaddingBottom, move_x + _content_width, _BTop + PaddingTop));
            }
            if (_doc != null)
            {
                state.BeginText(draw);
                if (move_x != 0 || move_y != 0)
                {
                    state.TranslateTLM(move_x, -move_y);
                    draw.TranslateTLM(move_x, -move_y);
                }
                var cls = RenderText(draw, state, true);
                state.EndText(draw);

                //Draws images and underlines
                cDraw.DrawBlock(draw, cls, state);
            }


            if (HasBorder)
            {
                if (HasBorderRadius || Overflow == Overdraw.Clipped && _height != null)
                {
                    //Removes the clipping
                    state = state.Restore(draw);
                }

                if (BoderUniform)
                {
                    //Simple square or rounded border
                    if (BorderColorLeft != null)
                        BorderColorLeft.SetColor(state, false, draw);
                    if (DashLeft != null)
                        draw.SetStrokeDashAr(DashLeft.Value);
                    draw.StrokeWidth = _BLeft;

                    if (HasBorderRadius)
                    {
                        DrawRoundRect(stroke, draw);
                        draw.ClosePath();
                    }
                    else
                        draw.RectAt(_BLeft / 2,
                            -_BTop - inner_height - _BBottom / 2,
                            InnerWidth + _BLeft / 2 + _BRight / 2,
                            inner_height + _BBottom / 2 + _BTop / 2);
                    draw.DrawPathNZ(false, true, false);
                }
                else
                {
                    //Draws the borders in four sections, each with its own stroke, color, tickness, and such.
                    if (clip == null)
                    {
                        //Creates the stroke geometry
                        stroke = CreatePoints(
                            _BLeft / 2,
                            -_BTop - inner_height - _BBottom / 2,
                            InnerWidth + _BLeft / 2 + _BRight / 2,
                            inner_height + _BBottom / 2 + _BTop / 2);

                        //Creates the inner geometry.
                        clip = CreatePoints(_BLeft, -_BTop - inner_height, InnerWidth, inner_height);

                        //Adjusts it to the stroke geometry
                        clip = Adjust(stroke, clip);
                    }

                    //Creates the outer geometry
                    var outer = CreatePoints(
                        0,
                        -_BTop - inner_height - _BBottom,
                        InnerWidth + _BLeft + _BRight,
                        inner_height + _BBottom + _BTop);
                    outer = Adjust(stroke, outer);

                    {
                        //How the points are layed out:
                        //       -P1---------P2
                        //      /              \
                        //     P0              P3
                        //     |               |
                        //     P7              P4
                        //      \              /
                        //       -P6--------P5-
                        //This code draws the borders as four independant strokes. To allow for borders of different
                        //ticknesses and corner rounding the code first creates a clip path, then strokes with the
                        //largest stroke width.
                        //
                        //Note that when corners are uncurved then the corner points (say, P1 and P0) are exactly the
                        //same. This is a special case where it's impossible to draw a curve between the points.
                        //
                        //To compensate for this the algorithm skips on drawing curves when this is the case. Do note
                        //that while the Ajust(..) function makes sure such corner points are exactly the same,
                        //while CreatePoints( .. ) does not. I.e. one can not compare points that comes from CreatePoints
                        //for equality using xPoint.Equals(..)
                        //
                        //To work around this we instead test the BorderRadius porperties directly, i.e:
                        // "!Util.Real.Same(BorderRadiusUL, 0, _min_prec)" instead of "stroke[0] != stroke[1]"
                        //
                        //The only drawback of this workaround is that this algorithm now depends on these properties,
                        //also the padding isn't entierly right. (I.e. "draw.LineTo(stroke[2].X + BorderRight, stroke[2].Y);"
                        //should be "draw.LineTo(outer[2].X, stroke[2].Y);").
                        //
                        //But I find the former more readable, and any excess padding is clipped away.
                        //
                        //Issues:
                        //
                        // 1. Thin white line
                        //    There may be a thin white line between the borders. I don't know any good way to fix this.
                        //    For now one have to use overdraw.
                        //
                        // 2. Midpoint of curves.
                        //    The current method of finding midpoints are by using hardcoded values. This seems to work
                        //    well enough when lines are of uniform tickness, not so much when lines are of non-uniform
                        //    tichness. 

                        var largest_stroke = Math.Max(_BBottom, Math.Max(_BRight, Math.Max(_BLeft, _BTop)));
                        state.SetStrokeWidth(largest_stroke, draw);

                        //Creates right geometry
                        if (_BRight > 0)
                        {
                            state.Save(draw);
                            state.SetStrokeDashAr(DashRight, draw);
                            BorderColorRight.SetColor(state, false, draw);
                            draw.SetClip(xFillRule.Nonzero);
                            DrawVBorder(outer[2], outer[3], outer[4], outer[5],
                                      clip[2], clip[3], clip[4], clip[5],
                                      draw
#if OVERDRAW
, IsSimilar(BorderColorRight, BorderColorTop),
                                      IsSimilar(BorderColorBottom, BorderColorRight)
#endif
);
                            draw.ClosePath();
                            draw.DrawPathNZ(false, false, false);

                            //Draws the right line
                            if (!Util.Real.Same(_RadiusUR, 0, _min_prec))
                            {
                                draw.MoveTo(stroke[2].X, stroke[2].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[2], stroke[3].X, stroke[2].Y, stroke[3].X, stroke[3].Y);
#else
                                draw.CurveTo(stroke[3].X, stroke[2].Y, stroke[3].X, stroke[3].Y);
#endif
                            }
                            else
                                draw.MoveTo(stroke[2].X, stroke[2].Y + _BTop);
                            if (!Util.Real.Same(_RadiusLR, 0, _min_prec))
                            {
                                draw.LineTo(stroke[4].X, stroke[4].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[4], stroke[4].X, stroke[5].Y, stroke[5].X, stroke[5].Y);
#else
                                draw.CurveTo(stroke[4].X, stroke[5].Y, stroke[5].X, stroke[5].Y);
#endif
                            }
                            else
                                draw.LineTo(stroke[4].X, stroke[4].Y - _BBottom);
                            draw.DrawPathNZ(false, true, false);
                            state = state.Restore(draw);
                        }

                        //Creates left geometry
                        if (_BLeft > 0)
                        {
                            state.Save(draw);
                            state.SetStrokeDashAr(DashLeft, draw);
                            BorderColorLeft.SetColor(state, false, draw);
                            draw.SetClip(xFillRule.Nonzero);
                            DrawVBorder(outer[6], outer[7], outer[0], outer[1],
                                      clip[6], clip[7], clip[0], clip[1],
                                      draw
#if OVERDRAW
, IsSimilar(BorderColorLeft, BorderColorBottom),
                                      IsSimilar(BorderColorLeft, BorderColorTop)
#endif
);
                            draw.ClosePath();
                            draw.DrawPathNZ(false, false, false);

                            //Draws the left line
                            if (!Util.Real.Same(_RadiusLL, 0, _min_prec))
                            {
                                draw.MoveTo(stroke[6].X, stroke[6].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[6], stroke[7].X, stroke[6].Y, stroke[7].X, stroke[7].Y);
#else
                                draw.CurveTo(stroke[7].X, stroke[6].Y, stroke[7].X, stroke[7].Y);
#endif
                            }
                            else
                                draw.MoveTo(stroke[6].X, stroke[6].Y - _BTop);
                            if (!Util.Real.Same(_RadiusUL, 0, _min_prec))
                            {
                                draw.LineTo(stroke[0].X, stroke[0].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[0], stroke[0].X, stroke[1].Y, stroke[1].X, stroke[1].Y);
#else
                                draw.CurveTo(stroke[0].X, stroke[1].Y, stroke[1].X, stroke[1].Y);
#endif
                            }
                            else
                                draw.LineTo(stroke[0].X, stroke[0].Y + _BTop);
                            draw.DrawPathNZ(false, true, false);
                            state = state.Restore(draw);
                        }

                        //Creates the top geometry
                        if (_BTop > 0)
                        {
                            state.Save(draw);
                            state.SetStrokeDashAr(DashTop, draw);
                            BorderColorTop.SetColor(state, false, draw);
                            draw.SetClip(xFillRule.Nonzero);
                            DrawHBorder(outer[0], outer[1], outer[2], outer[3],
                                      clip[0], clip[1], clip[2], clip[3],
                                      draw
#if OVERDRAW
, IsSimilar(BorderColorLeft, BorderColorTop),
                                      IsSimilar(BorderColorTop, BorderColorRight)
#endif
);
                            draw.ClosePath();
                            draw.DrawPathNZ(false, false, false);

                            //Draws the top line
                            if (!Util.Real.Same(_RadiusUL, 0, _min_prec))
                            {
                                draw.MoveTo(stroke[0].X, stroke[0].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[0], stroke[0].X, stroke[1].Y, stroke[1].X, stroke[1].Y);
#else
                                draw.CurveTo(stroke[0].X, stroke[1].Y, stroke[1].X, stroke[1].Y);
#endif
                            }
                            else
                                draw.MoveTo(stroke[0].X - _BLeft, stroke[0].Y);
                            if (!Util.Real.Same(_RadiusUR, 0, _min_prec))
                            {
                                draw.LineTo(stroke[2].X, stroke[2].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[2], stroke[3].X, stroke[2].Y, stroke[3].X, stroke[3].Y);
#else
                                draw.CurveTo(stroke[3].X, stroke[2].Y, stroke[3].X, stroke[3].Y);
#endif
                            }
                            else
                                draw.LineTo(stroke[2].X + _BRight, stroke[2].Y);
                            draw.DrawPathNZ(false, true, false);
                            state = state.Restore(draw);
                        }

                        //Creates bottom geometry
                        if (_BBottom > 0)
                        {
                            state.Save(draw);
                            state.SetStrokeDashAr(DashBottom, draw);
                            BorderColorBottom.SetColor(state, false, draw);
                            draw.SetClip(xFillRule.Nonzero);
                            DrawHBorder(outer[4], outer[5], outer[6], outer[7],
                                      clip[4], clip[5], clip[6], clip[7],
                                      draw
#if OVERDRAW
, IsSimilar(BorderColorRight, BorderColorBottom),
                                      IsSimilar(BorderColorBottom, BorderColorLeft)
#endif
);
                            draw.ClosePath();
                            draw.DrawPathNZ(false, false, false);

                            //Draws the bottom line
                            if (!Util.Real.Same(_RadiusLR, 0, _min_prec))
                            {
                                draw.MoveTo(stroke[4].X, stroke[4].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[4], stroke[4].X, stroke[5].Y, stroke[5].X, stroke[5].Y);
#else
                                draw.CurveTo(stroke[4].X, stroke[5].Y, stroke[5].X, stroke[5].Y);
#endif
                            }
                            else
                                draw.MoveTo(stroke[4].X + _BRight, stroke[4].Y);
                            if (!Util.Real.Same(_RadiusLL, 0, _min_prec))
                            {
                                draw.LineTo(stroke[6].X, stroke[6].Y);
#if QUADRACTIC_CURVES
                                cDraw.CurveTo(draw, stroke[6], stroke[7].X, stroke[6].Y, stroke[7].X, stroke[7].Y);
#else
                                draw.CurveTo(stroke[7].X, stroke[6].Y, stroke[7].X, stroke[7].Y);
#endif
                            }
                            else
                                draw.LineTo(stroke[6].X - _BLeft, stroke[6].Y);
                            draw.DrawPathNZ(false, true, false);
                            state = state.Restore(draw);
                        }
                    }
                }
            }
            else
            {
                if (Overflow == Overdraw.Clipped && _height != null)
                    state = state.Restore(draw);
            }

            state = state.Restore(draw);
        }

        private void DrawVBorder(xPoint outer_p0, xPoint outer_p1, xPoint outer_p2, xPoint outer_p3,
                                xPoint inner_p0, xPoint inner_p1, xPoint inner_p2, xPoint inner_p3,
                                IDraw draw
#if OVERDRAW
, bool overdraw_start, bool overdraw_end
#endif
)
        {
#if QUADRACTIC_CURVES
            const double t_mid_ul = 0.50, t_mid_ur = 0.5;
#else
            const double t_mid_ul = 0.40, t_mid_ur = 0.3;
#endif
#if OVERDRAW
            double od2 = Math.Max(_min_prec, Overdraw_corner),
                overdraw_s = overdraw_start ? Overdraw_curve : 0,
                overdraw_e = overdraw_end ? Overdraw_curve : 0;
            if (outer_p0.X > inner_p0.X) od2 *= -1;
#else
            const double overdraw_s = 0, overdraw_e = 0;
#endif
#if QUADRACTIC_CURVES
            xPoint ctrl1, ctrl2;
#endif
            xPoint[] start;
            if (outer_p0 != outer_p1)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(outer_p1.X, outer_p0.Y);
                ctrl2 = outer_p1;
                QuadraticToCubic(outer_p0, ref ctrl1, ref ctrl2);
                start = SplitBezierLast(outer_p0, ctrl1, ctrl2, outer_p1, t_mid_ul - overdraw_s);
#else
                start = SplitBezierLast(outer_p0, new xPoint(outer_p1.X, outer_p0.Y), outer_p1, outer_p1, t_mid_ul - overdraw_s);
#endif
                draw.MoveTo(start[2].X, start[2].Y);
                draw.CurveTo(start[1].X, start[1].Y, start[0].X, start[0].Y, outer_p1.X, outer_p1.Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_start)
                {
                    draw.MoveTo(outer_p0.X + od2, outer_p0.Y);
                    draw.LineTo(outer_p0.X, outer_p0.Y);
                }
                else
                    draw.MoveTo(outer_p0.X, outer_p0.Y);
#else
                draw.MoveTo(outer_p0.X, outer_p0.Y);
#endif
            }
            draw.LineTo(outer_p2.X, outer_p2.Y);
            if (outer_p2 != outer_p3)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(outer_p2.X, outer_p3.Y);
                ctrl2 = outer_p3;
                QuadraticToCubic(outer_p2, ref ctrl1, ref ctrl2);
                start = SplitBezierFirst(outer_p2, ctrl1, ctrl2, outer_p3, t_mid_ur + overdraw_e);
#else
                start = SplitBezierFirst(outer_p2, new xPoint(outer_p2.X, outer_p3.Y), outer_p3, outer_p3, t_mid_ur + overdraw_e);
#endif
                draw.CurveTo(start[0].X, start[0].Y, start[1].X, start[1].Y, start[2].X, start[2].Y);
            }
#if OVERDRAW
            else if (overdraw_end)
            {
                draw.LineTo(outer_p2.X + od2, outer_p2.Y);
            }
#endif
            if (inner_p3 != inner_p2)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(inner_p2.X, inner_p3.Y);
                ctrl2 = inner_p3;
                QuadraticToCubic(inner_p2, ref ctrl1, ref ctrl2);
                start = SplitBezierFirst(inner_p2, ctrl1, ctrl2, inner_p3, t_mid_ur + overdraw_e);
#else
                start = SplitBezierFirst(inner_p2, new xPoint(inner_p2.X, inner_p3.Y), inner_p3, inner_p3, t_mid_ur + overdraw_e);
#endif
                draw.LineTo(start[2].X, start[2].Y);
                draw.CurveTo(start[1].X, start[1].Y, start[0].X, start[0].Y, inner_p2.X, inner_p2.Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_end)
                    draw.LineTo(inner_p3.X, inner_p3.Y + od2);
                else
                    draw.LineTo(inner_p3.X, inner_p3.Y);
#endif
                draw.LineTo(inner_p3.X, inner_p3.Y);
            }
            if (inner_p1 != inner_p0)
            {
                draw.LineTo(inner_p1.X, inner_p1.Y);
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(inner_p1.X, inner_p0.Y);
                ctrl2 = inner_p1;
                QuadraticToCubic(inner_p0, ref ctrl1, ref ctrl2);
                start = SplitBezierLast(inner_p0, ctrl1, ctrl2, inner_p1, t_mid_ul - overdraw_s);
#else
                start = SplitBezierLast(inner_p0, new xPoint(inner_p1.X, inner_p0.Y), inner_p1, inner_p1, t_mid_ul - overdraw_s);
#endif
                draw.CurveTo(start[0].X, start[0].Y, start[1].X, start[1].Y, start[2].X, start[2].Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_start)
                    draw.LineTo(inner_p1.X, inner_p1.Y - od2);
                else
                    draw.LineTo(inner_p1.X, inner_p1.Y);
#else
                draw.LineTo(inner_p1.X, inner_p1.Y);
#endif
            }
        }

        private void DrawHBorder(xPoint outer_p0, xPoint outer_p1, xPoint outer_p2, xPoint outer_p3,
                                xPoint inner_p0, xPoint inner_p1, xPoint inner_p2, xPoint inner_p3,
                                IDraw draw
#if OVERDRAW
, bool overdraw_start, bool overdraw_end
#endif
)
        {
#if QUADRACTIC_CURVES
            const double t_mid_ul = 0.50, t_mid_ur = 0.5;
#else
            const double t_mid_ul = 0.30, t_mid_ur = 0.4;
#endif
#if OVERDRAW
            double od2 = Math.Max(_min_prec, Overdraw_corner),
                overdraw_s = overdraw_start ? Overdraw_curve : 0,
                overdraw_e = overdraw_end ? Overdraw_curve : 0;
            if (outer_p0.Y < inner_p0.Y) od2 *= -1;
#else
            const double overdraw_e = 0, overdraw_s = 0;
#endif
#if QUADRACTIC_CURVES
            xPoint ctrl1, ctrl2;
#endif
            xPoint[] start;
            if (outer_p0 != outer_p1)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(outer_p0.X, outer_p1.Y);
                ctrl2 = outer_p1;
                QuadraticToCubic(outer_p0, ref ctrl1, ref ctrl2);
                start = SplitBezierLast(outer_p0, ctrl1, ctrl2, outer_p1, t_mid_ul - overdraw_s);
#else
                start = SplitBezierLast(outer_p0, new xPoint(outer_p0.X, outer_p1.Y), outer_p1, outer_p1, t_mid_ul - overdraw_s);
#endif
                draw.MoveTo(start[2].X, start[2].Y);
                draw.CurveTo(start[1].X, start[1].Y, start[0].X, start[0].Y, outer_p1.X, outer_p1.Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_start)
                {
                    draw.MoveTo(outer_p0.X, outer_p0.Y - od2);
                    draw.LineTo(outer_p0.X, outer_p0.Y);
                }
                else
                    draw.MoveTo(outer_p0.X, outer_p0.Y);
#else
                draw.MoveTo(outer_p0.X, outer_p0.Y);
#endif
            }
            draw.LineTo(outer_p2.X, outer_p2.Y);
            if (outer_p2 != outer_p3)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(outer_p3.X, outer_p2.Y);
                ctrl2 = outer_p3;
                QuadraticToCubic(outer_p2, ref ctrl1, ref ctrl2);
                start = SplitBezierFirst(outer_p2, ctrl1, ctrl2, outer_p3, t_mid_ur + overdraw_e);
#else
                start = SplitBezierFirst(outer_p2, new xPoint(outer_p3.X, outer_p2.Y), outer_p3, outer_p3, t_mid_ur + overdraw_e);
#endif
                draw.CurveTo(start[0].X, start[0].Y, start[1].X, start[1].Y, start[2].X, start[2].Y);
            }
#if OVERDRAW
            else
            {
                if (overdraw_end)
                    draw.LineTo(outer_p2.X, outer_p2.Y - od2);
            }
#endif
            if (inner_p3 != inner_p2)
            {
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(inner_p3.X, inner_p2.Y);
                ctrl2 = inner_p3;
                QuadraticToCubic(inner_p2, ref ctrl1, ref ctrl2);
                start = SplitBezierFirst(inner_p2, ctrl1, ctrl2, inner_p3, t_mid_ur + overdraw_e);
#else
                start = SplitBezierFirst(inner_p2, new xPoint(inner_p3.X, inner_p2.Y), inner_p3, inner_p3, t_mid_ur + overdraw_e);
#endif
                draw.LineTo(start[2].X, start[2].Y);
                draw.CurveTo(start[1].X, start[1].Y, start[0].X, start[0].Y, inner_p2.X, inner_p2.Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_end)
                    draw.LineTo(inner_p3.X + od2, inner_p3.Y);
                else
                    draw.LineTo(inner_p3.X, inner_p3.Y);
#endif
                draw.LineTo(inner_p3.X, inner_p3.Y);
            }
            if (inner_p1 != inner_p0)
            {
                draw.LineTo(inner_p1.X, inner_p1.Y);
#if QUADRACTIC_CURVES
                ctrl1 = new xPoint(inner_p0.X, inner_p1.Y);
                ctrl2 = inner_p1;
                QuadraticToCubic(inner_p0, ref ctrl1, ref ctrl2);
                start = SplitBezierLast(inner_p0, ctrl1, ctrl2, inner_p1, t_mid_ul - overdraw_s);
#else
                start = SplitBezierLast(inner_p0, new xPoint(inner_p0.X, inner_p1.Y), inner_p1, inner_p1, t_mid_ul - overdraw_s);
#endif
                draw.CurveTo(start[0].X, start[0].Y, start[1].X, start[1].Y, start[2].X, start[2].Y);
            }
            else
            {
#if OVERDRAW
                if (overdraw_start)
                    draw.LineTo(inner_p1.X - od2, inner_p1.Y);
                else
                    draw.LineTo(inner_p1.X, inner_p1.Y);
#else
                draw.LineTo(inner_p1.X, inner_p1.Y);
#endif
            }
        }

        /// <summary>
        /// Splits a Bezier curve. Returns the second half.
        /// </summary>
        private xPoint[] SplitBezierLast(xPoint start, xPoint ctrl1, xPoint ctrl2, xPoint end, double t)
        {
            return SplitBezierFirst(end, ctrl2, ctrl1, start, 1 - t);
        }

        /// <summary>
        /// Splits a Bezier curve. Returns the first half.
        /// </summary>
        private xPoint[] SplitBezierFirst(xPoint start, xPoint ctrl1, xPoint ctrl2, xPoint end, double t)
        {
            //First we find the midpoint, which will also serve as a start/end point
            //const double t = 0.5;

            double x1 = start.X, y1 = start.Y,
                   x2 = ctrl1.X, y2 = ctrl1.Y,
                   x3 = ctrl2.X, y3 = ctrl2.Y,
                   x4 = end.X, y4 = end.Y;

            var x12 = (x2 - x1) * t + x1;
            var y12 = (y2 - y1) * t + y1;

            var x23 = (x3 - x2) * t + x2;
            var y23 = (y3 - y2) * t + y2;

            var x34 = (x4 - x3) * t + x3;
            var y34 = (y4 - y3) * t + y3;

            var x123 = (x23 - x12) * t + x12;
            var y123 = (y23 - y12) * t + y12;

            var x234 = (x34 - x23) * t + x23;
            var y234 = (y34 - y23) * t + y23;

            var x1234 = (x234 - x123) * t + x123;
            var y1234 = (y234 - y123) * t + y123;

            return new xPoint[] { new xPoint(x12, y12), new xPoint(x123, y123), new xPoint(x1234, y1234) };
        }

        /// <summary>
        /// Draws just the text, no border, images or underlines
        /// </summary>
        /// <param name="draw">Surface to draw to</param>
        /// <param name="state">Current render state</param>
        /// <param name="move_text">Whenever to move the text down by its height</param>
        public cBlock[][] RenderText(IDraw draw, cRenderState state, bool move_text)
        {
            if (state.GS != GS.Text)
                throw new NotSupportedException("State must be text");

            cBlock[] blocks;

            if (state.Tf == null || !ReferenceEquals(state.Tf, _org_state.Tf) || state.Tfs != _org_state.Tfs)
            {
                state.Tf = _org_state.Tf;
                state.Tfs = _org_state.Tfs;
                if (state.Tf == null)
                    throw new NotSupportedException("Default font required");
                draw.SetFont(state.Tf, state.Tfs);
            }
#if DIRECT_DRAW
            var text_renderer = new TextRenderer(state, draw);
#else
            var text_renderer = new TextRenderer(state);
            CompiledLine cl;
#endif
            //Setting reset state false, saves us some needless Tw commands when column aligning the
            //text. 
            text_renderer.LsbAdjustText = _strip_sb;
            text_renderer.ResetState = false;
            text_renderer.ResetState_tw = state.Tw;
            var compiled_lines = new List<cBlock[]>(_line_count);
            int line_nr = 0;
            double move = 0, current_height = 0;
            _min_prec = 1 / Math.Pow(10, draw.Precision);

            //When has_width = true it means that the induvidual lines has width. When
            //false, we have to supply a width to use for text alignment.
            double? render_width;
            if (has_width)
                render_width = null;
            else
                render_width = _content_width;

#if DONT_MOVE_OPTIMIZATION
            //To avoid a needless Td, we special case this particular condition.
            move_text = move_text || !(PositionOver && !HasBorder && BackgroundColor == null);
#endif
            xIntRange par_range = new xIntRange();
            for (int index = 0; index < _par_metrics.Length; index++)
            {
                var par = _par_metrics[index];
                par_range.Max = par_range.Min + par.CharCount;
                if (!par_range.Overlaps(_range))
                {
                    if (_range.Max < par_range.Min)
                        break;
                    par_range.Min = par_range.Max + 1;
                    continue;
                }

                //Since ResetState = false, tw must be reset manually when not doing column rendering, in case the previous paragraph was rendered with
                //column rendering (as column rendering modify Tw and don't reset it back).
                if (!text_renderer.ResetState && par.Par.Alignment < chParagraph.Alignement.Column && text_renderer.State.Tw != text_renderer.ResetState_tw)
                    text_renderer.SetWordGap(text_renderer.ResetState_tw);

                int par_count = par.LineCount;
                if (par_count == 0) continue;

                var par_line = par.GetLine(0);
                //Note that par_line.Range is the range within a paragraph, not the range from the starting point of the document. Thus, if there's
                //more than one paragraph, this assert alwyas triggers. 
                //Debug.Assert(new xIntRange(par_line.Range.Min + par_range.Min, Math.Max(par_line.Range.Max, 0) + par_range.Min).Overlaps(_range), "Likely a Layout() bug.");

                if (line_nr != 0)
                {
                    //Move the paragraph down, except the first paragraph. The first move
                    //is the height of the first line in the paragrap. Then the gap between
                    //paragraphs are added to this.
                    move = -par_line.LineHeight;
#if MUL_PAR_GAP_WITH_LH
                    move = move - par.ParGap * _paragraph_gap;
#else
                    move = move - par_line.FontHeight * _paragraph_gap;
#endif

                }
                else
                {
                    //Moves down the height of the very first line.
                    //Using FontHeight as one should not include "Default height" in this.
                    move = -par_line.LineHeight;
                }

                if (Overflow == Overdraw.Hidden && _height != null)
                {
                    current_height -= move;
                    if (current_height > _height.Value)
                        break;
                }

                line_nr++;

                if (!move_text)
                    move_text = true;
                else
                {
                    state.TranslateTLM(0, move);
                    draw.TranslateTLM(0, move);
                }

                for (int c = 0; ; )
                {
                    //Renders the line
                    if (!par_line.Render(text_renderer, render_width))
                        break;

                    if (!(++c < par_count))
                    {
                        //Draws the line
#if DIRECT_DRAW
                        blocks = text_renderer.MakeBlocks();
                        if (blocks != null)
                            compiled_lines.Add(blocks);
#else
                        cl = text_renderer.Make();
                        if (cl.Blocks != null) compiled_lines.Add(cl.Blocks);
                        draw.Draw(cl);
#endif

                        break;
                    }
                    //Gets the height of the next line.
                    par_line = par.GetLine(c);
                    move = -par_line.LineHeight;

                    //Draws the line
#if DIRECT_DRAW
                    blocks = text_renderer.MakeBlocks();
                    if (blocks != null)
                        compiled_lines.Add(blocks);
#else
                    cl = text_renderer.Make();
                    if (cl.Blocks != null) compiled_lines.Add(cl.Blocks);
                    draw.Draw(cl);
#endif

                    if (Overflow == Overdraw.Hidden && _height != null)
                    {
                        current_height -= move;
                        if (current_height > _height.Value)
                            break;
                    }

                    //Moves down a line.
                    if (par.NumLineHeights(c + 1, -move, _min_prec) > 0 || state.Tl == 0)
                    {
                        state.SetTlandTransTLM(0, move);
                        draw.SetTlandTransTLM(0, move);
                    }
                    else if (Util.Real.Same(state.Tl, -move, _min_prec))
                    {
                        state.TranslateTLM();
                        draw.TranslateTLM();
                    }
                    else
                    {
                        state.TranslateTLM(0, move);
                        draw.TranslateTLM(0, move);
                    }
                }

                par_range.Min = par_range.Max + 1;
            }

            state.BreakWordCharacter = null;
            return compiled_lines.Count > 0 ? compiled_lines.ToArray() : null;
        }

        #endregion

        #region Helper metods and classes

        public enum Overdraw
        {
            /// <summary>
            /// Hides any text that can't fit inside the text box
            /// </summary>
            Hidden,

            /// <summary>
            /// Clips text that extends beyond the boundaries of the textbox
            /// </summary>
            Clipped,

            /// <summary>
            /// All text in the textbox is rendered, regardless
            /// </summary>
            Visible
        }

        bool IsSimilar(cColor c1, cColor c2)
        {
            if (ReferenceEquals(c1, c2))
                return true;
            if (c1 == null || c2 == null)
                return false;
            var y1 = new YUVColor(c1.Color);
            var y2 = new YUVColor(c2.Color);
            var dist = y1.Distance(y2);
            return dist < Overdraw_corner;
        }

        #endregion
    }
}
