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
using PdfLib.Compose.Layout;
using PdfLib.Compose.Layout.Internal;

namespace PdfLib.Compose.Text
{
    /// <summary>
    /// A square textbox that can render a document.
    /// </summary>
    /// <remarks>
    /// This roadmap is a bit obsolete. Step 1, 2 and 4 should still be implemented, but see comments marked with *. 
    /// 
    /// Roadmap:
    ///  1. Indent right/left + hang
    ///  
    ///     This should be the next feature. It's already half-implemented, and will be usefull for implementing the
    ///     next feature.
    ///   * This feature is still usefull and realtivly simple to implement. However, only "hang" is of interest to
    ///     the new HTML inspired layout engine. 
    ///  2. Lists
    ///  
    ///     Faces some of the same challanges as Indent. Naturally one also need to draw the character marking the list.
    ///   * Lists don't yet exist in cDiv, and thus shouldn't be dificult to implement. Though it may be better to leave 
    ///     this unimplemented and do a cList instead.
    ///  3. Add cBox objects to lines (prepwork for step 5)
    ///  
    ///     It's already possible to add XObjects. This can be extended to objects that implements a certain interface.
    ///     These objects are delay rendered, so there shouldn't be any issues. The only requirement is that they
    ///     have a size.
    ///   * Trivial to implemet, but I'm not sure if there's any real use for this.
    ///  4. Extend the underline functionality to be more generic.
    ///  
    ///     There are two challanges here: Post and pre text rendering. Post is already implemented, and works, while
    ///     pre is a bit harder. Do note, that for rendering underlines, the current impl. is fine. This is for other
    ///     things (like marking the text backround yellow - which can now be achived using cDiv and cText). 
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
    ///   * The stuff this would be useful for is possible to accomplish using cText markup. Might still be worth implimenting. 
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
    ///   * This feature isn't really ambitious enough, while making lot of changes to working code. 
    ///  6. Horizontal divider. (See region idea instead)
    ///  7. Tables (Use cTable instead)
    ///  
    ///     Idea. A table can be made out of text boxes. They can already size themselves and draw borders. Making this more of
    ///           a grid layout manager. Since tables are blocks to, they can then be put inside a textbox.
    ///           
    /// Regions idea:
    /// Certain things, like tables, horzontal dividers, and to some limited extend, flow, can be handles by using the HTML inspired
    /// layout system, or chTBChain.
    /// 
    /// Now, for sophisticated text layout I think it's best to extend the chTBChain with "regions" instead. A region is a rectangular
    /// space that can be filled with polygons. When text is layed out in a region, it flows around these polygons. With that one can
    /// emulate flow text to a suffucient degree, and there's no need to overtly complicate the existing text layout code.
    /// 
    /// Here's a libary that can freely be used, with a lisence that is very friendly. 
    /// http://www.angusj.com/delphi/clipper/documentation/Docs/Overview/_Body.htm
    /// 
    /// Now, regions is not a perfect replacment for "flow: left/right", however with PDF files being fairly static, you can emulate
    /// flow like behavior. Then you also get the possibility of flowing text around non-rectangular objects, or filling text into a
    /// circle. Neat things like that. 
    /// 
    ///     Regions algo:
    ///     The step 5. algo is a little complex, I think we can get away with something simpler.
    ///     1. Use the font height of the first letter to create a initial rectangular polygon, and use the intersection libary
    ///        to find intersections.
    ///     2. Measure the height of the text that would idealy fit into this avalible room (just add all free space together).
    ///     3. Repeat step 1, just with the new height. (Unless the height is equal)
    ///     4. Now lay out the text. Regardless of what max font height we end up with, use the font height from step 3.
    /// </remarks>
    public class chTextBox : cBox
    {
        #region Variables and properties

        private readonly chTextLayout _text;
        private bool _stop_event = false;
        private readonly cStyle _style;

        #region Position

        /// <summary>
        /// If set, the textbox will have a position.
        /// </summary>
        public xPoint Position = new xPoint();

        /// <summary>
        /// If true, the text box will position itself over the point
        /// it was drawn instead of under it
        /// </summary>
        public bool PositionOver = false;

        /// <summary>
        /// Rotates the textbox
        /// </summary>
        public double RotationAngle
        {
            get { return Style.RotationAngle; }
            set { _style.RotationAngle = value; }
        }

        /// <summary>
        /// Point to rotate the textbox around. By default, center point is used.
        /// For upper right, set (1,1), lower left, (0,0) etc.
        /// </summary>
        public xPoint RotationPoint
        {
            get { return Style.RotationPoint; }
            set { _style.RotationPoint = value; }
        }

        #endregion

        #region Appearance

        /// <summary>
        /// How the textbox handles text that overflows its boundaries.
        /// </summary>
        public Overdraw Overflow = Overdraw.Hidden;

        /// <summary>
        /// Whenever or not to strip the side bearings from the text when
        /// calculating content width. Also note, when true, left and right aligned
        /// text is postitioned perfectly at the edge.
        /// </summary>
        public bool StripSideBearings
        {
            get { return _text.StripSideBearings; }
            set { _text.StripSideBearings = value; }
        }

        /// <summary>
        /// Font used if no font is defined.
        /// </summary>
        public cFont DefaultFont 
        { 
            get { return _text.DefaultFont; }
            set { _text.DefaultFont = value; }
        }

        /// <summary>
        /// Standar size for fonts
        /// </summary>
        public double DefaultFontSize
        {
            get { return _text.DefaultFontSize; }
            set { _text.DefaultFontSize = value; }
        }

        /// <summary>
        /// Color of non-outline fonts
        /// </summary>
        public cBrush DefaultFontColor
        {
            get { return _text.DefaultFontColor; }
            set { _text.DefaultFontColor = value; }
        }

        /// <summary>
        /// Color of outlined text
        /// </summary>
        public cBrush DefaultOutlineColor
        {
            get { return _text.DefaultOutlineColor; }
            set { _text.DefaultOutlineColor = value; }
        }

        /// <summary>
        /// Thickness of outlines
        /// </summary>
        public double DefaultOutlineWidth
        {
            get { return _text.DefaultOutlineWidth; }
            set { _text.DefaultOutlineWidth = value; }
        }


        /// <summary>
        /// Character used for breaking words. Use '\0' for no character
        /// </summary>
        public char BreakWordCharacter
        {
            get { return _text.BreakWordCharacter; }
            set { _text.BreakWordCharacter = value; }
        }

        /// <summary>
        /// Sets right to left text flow.
        /// </summary>
        public bool RightToLeft
        {
            get { return _text.RightToLeft; }
            set { _text.RightToLeft = value; }
        }

        public float MarginLeft
        {
            get { return Style.MarginLeft.Value; }
            set { _style.MarginLeft = new cSize(value); }
        }
        public float MarginRight
        {
            get { return Style.MarginRight.Value; }
            set { _style.MarginRight = new cSize(value); }
        }
        public float MarginTop
        {
            get { return Style.MarginTop.Value; }
            set { _style.MarginTop = new cSize(value); }
        }
        public float MarginBottom
        {
            get { return Style.MarginBottom.Value; }
            set { _style.MarginBottom = new cSize(value); }
        }

        public float PaddingLeft
        {
            get { return Style.PaddingLeft.Value; }
            set { _style.PaddingLeft = new cSize(value); }
        }
        public float PaddingRight
        {
            get { return Style.PaddingRight.Value; }
            set { _style.PaddingRight = new cSize(value); }
        }
        public float PaddingTop
        {
            get { return Style.PaddingTop.Value; }
            set { _style.PaddingTop = new cSize(value); }
        }
        public float PaddingBottom
        {
            get { return Style.PaddingBottom.Value; }
            set { _style.PaddingBottom = new cSize(value); }
        }
        public float? Padding
        {
            get
            {
                var pad = Style.Padding;
                return pad != null ? pad.Value.Value : (float?) null;
            }
            set
            {
                _style.Padding = value == null ? (cSize?) null : new cSize(value.Value);
            }
        }
        
        /// <summary>
        /// Border Radius Lower Left corner
        /// </summary>
        public float BorderRadiusLL
        {
            get { return Style.BorderRadiusLL.Value; }
            set { _style.BorderRadiusLL = new cSize(value); }
        }
        /// <summary>
        /// Border Radius Lower Right corner
        /// </summary>
        public float BorderRadiusLR
        {
            get { return Style.BorderRadiusLR.Value; }
            set { _style.BorderRadiusLR = new cSize(value); }
        }
        /// <summary>
        /// Border Radius Upper Left corner
        /// </summary>
        public float BorderRadiusUL
        {
            get { return Style.BorderRadiusUL.Value; }
            set { _style.BorderRadiusUL = new cSize(value); }
        }
        /// <summary>
        /// Border Radius Upper Right corner
        /// </summary>
        public float BorderRadiusUR
        {
            get { return Style.BorderRadiusUR.Value; }
            set { _style.BorderRadiusUR = new cSize(value); }
        }
        public float? BorderRadius
        {
            get
            {
                var pad = Style.BorderRadius;
                return pad != null ? pad.Value.Value : (float?)null;
            }
            set
            {
                _style.BorderRadius = value == null ? (cSize?)null : new cSize(value.Value);
            }
        }
        
        /// <summary>
        /// Left border width
        /// </summary>
        public float BorderLeft
        {
            get { return Style.BorderLeft.Value; }
            set { _style.BorderLeft = new cSize(value); }
        }
        /// <summary>
        /// Right border width
        /// </summary>
        public float BorderRight
        {
            get { return Style.BorderRight.Value; }
            set { _style.BorderRight = new cSize(value); }
        }
        /// <summary>
        /// Top border width
        /// </summary>
        public float BorderTop
        {
            get { return Style.BorderTop.Value; }
            set { _style.BorderTop = new cSize(value); }
        }
        /// <summary>
        /// Bottom border width
        /// </summary>
        public float BorderBottom
        {
            get { return Style.BorderBottom.Value; }
            set { _style.BorderBottom = new cSize(value); }
        }
        public float? BorderTichness
        {
            get
            {
                var pad = Style.BorderTichness;
                return pad != null ? pad.Value.Value : (float?) null;
            }
            set
            {
                _style.BorderTichness = value == null ? (cSize?)null : new cSize(value.Value);
            }
        }
        
        public new xDashStyle? DashLeft
        {
            get { return Style.DashLeft; }
            set { _style.DashLeft = value; }
        }
        public new xDashStyle? DashRight
        {
            get { return Style.DashRight; }
            set { _style.DashRight = value; }
        }
        public new xDashStyle? DashTop
        {
            get { return Style.DashTop; }
            set { _style.DashTop = value; }
        }
        public new xDashStyle? DashBottom
        {
            get { return Style.DashBottom; }
            set { _style.DashBottom = value; }
        }
        public xDashStyle? DashStyle
        {
            get { return Style.DashStyle; }
            set { _style.DashStyle = value; }
        }

        public new cColor BorderColorLeft
        {
            get { return Style.BorderColorLeft; }
            set { _style.BorderColorLeft = value; }
        }
        public new cColor BorderColorRight
        {
            get { return Style.BorderColorRight; }
            set { _style.BorderColorRight = value; }
        }
        public new cColor BorderColorTop
        {
            get { return Style.BorderColorTop; }
            set { _style.BorderColorTop = value; }
        }
        public new cColor BorderColorBottom
        {
            get { return Style.BorderColorBottom; }
            set { _style.BorderColorBottom = value; }
        }
        public cColor BorderColor
        {
            get { return Style.BorderColor; }
            set { _style.BorderColor = value; }
        }

        public cBrush BackgroundColor
        {
            get { return Style.BackgroundColor; }
            set { _style.BackgroundColor = value; }
        }

        #endregion

        #region Document

        public int LastDisplayedCharacter { get { return _text.LastDisplayedCharacter; } }

        /// <summary>
        /// Number of lines handled by the text box. If Overflow = hidden and height is set, then
        /// this will be the number of lines displayed.
        /// </summary>
        public int LineCount { get { return _text.LineCount; } }

        /// <summary>
        /// First and last character to show from the document.
        /// </summary>
        public xIntRange DocumentRange
        {
            get { return _text.DocumentRange; }
            set { _text.DocumentRange = value; }
        }

        public chDocument Document
        {
            get { return _text.Document; }
            set { _text.Document = value; }
        }
        internal void SetDocument(chDocument doc)
        { _text.SetDocument(doc); }

        #endregion

        #region Layout

        /// <summary>
        /// Determines the size and position of each block
        /// </summary>
        public void Layout()
        {
            base.Layout(this);
        }

        internal override bool VariableContentSize
        {
            get { return true; }
        }

        public override xSize ContentSize
        {
            get { return new xSize(_text.Width, _text.Height); }
        }

        /// <summary>
        /// Distance between paragraphs. The distance is multiplied
        /// with the lineheight of first line in the next paragraph.
        /// </summary>
        public double ParagraphGap
        {
            get { return _text.ParagraphGap; }
            set { _text.ParagraphGap = value; }
        }

        /// <summary>
        /// Standar width between lines.
        /// </summary>
        public double? DefaultLineHeight 
        {
            get { return _text.DefaultLineHeight; }
            set { _text.DefaultLineHeight = value; }
        }

        #endregion

        #region Textbox size

        /// <summary>
        /// Width of the textbox
        /// </summary>
        public new double Width
        {
            get { return VisFullWidth; }
            set { _style.Width = new cSize(value - MarginLeft - MarginRight); }
        }

        /// <summary>
        /// Width of the textbox, including the border and the padding
        /// </summary>
        public double BorderWidth
        {
            get { return VisWidth; }
            set { _style.Width = new cSize(value); }
        }        

        /// <summary>
        /// Width of the area inside the border of the textbox
        /// </summary>
        public double InnerWidth
        {
            get { return VisContentWidth + VisPaddingLeft + VisPaddingRight; }
            set { _style.Width = new cSize(value + BorderLeft + BorderRight); }
        }

        /// <summary>
        /// Width of the content of the text box
        /// </summary>
        public double ContentWidth
        {
            get { return VisContentWidth; }
            set
            {
                _style.Width = new cSize(BorderLeft + PaddingLeft + value + PaddingRight + BorderRight);
            }
        }

        public new double Height
        {
            get { return VisFullHeight; }
            set { _style.Height = new cSize(value - MarginTop - MarginBottom); }
        }

        public double InnerHeight
        {
            get { return VisContentHeight + PaddingTop + PaddingBottom; }
            set { _style.Height = new cSize(value + BorderTop + BorderBottom); }
        }

        /// <summary>
        /// Height of the textbox's content, set null to have the textbox automatically size itself
        /// </summary>
        public double ContentHeight
        {
            set 
            {
                _text.Height = value;
                DoLayoutContent = true;
            }
            get
            {
                return _text.Height;
            }
        }

        internal override double MaxDescent => _text.LastLineDescent;

        #endregion

        #endregion

        #region Init

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="content_width">Width of the text box's content. Padding comes in addition to this.</param>
        /// <param name="content_height">Height of the text box's content. Padding comes in addition to this.</param>
        public chTextBox(double content_width, double? content_height)
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
        public chTextBox(chDocument doc, cRenderState initial_state)
            : this(doc, null, initial_state)
        { }

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="doc">Text to display</param>
        /// <param name="font">Default font</param>
        /// <param name="font_size">Default font size</param>
        public chTextBox(string doc, cFont font, double font_size)
            : this(new chDocument(doc), null, new cRenderState(font, font_size))
        { }

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="doc">Text to display</param>
        /// <param name="content_width">Width of the text box's content. Padding comes in addition to this.</param>
        public chTextBox(string doc, double content_width) 
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
        internal chTextBox(string doc, cRenderState initial_state)
            : this(new chDocument(doc), null, initial_state)
        { }

        /// <summary>
        /// Creates a text box
        /// </summary>
        /// <param name="doc">Document to display</param>
        /// <param name="content_width">Width of the text box's content. Padding comes in addition to this.</param>
        public chTextBox(chDocument doc, double content_width)
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
        public chTextBox(chDocument doc, double? content_width, cRenderState initial_state)
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
        public chTextBox(chDocument doc, double? content_width, double? content_height, cRenderState initial_state)
        {
            _style = Style[0];
            _style.Clip = true;
            if (content_width != null)
            {
                _style.Width = new cSize((float)content_width);
                _style.Sizing = eBoxSizing.ContentBox;
            }
            _text = new chTextLayout(doc, content_width, content_height, initial_state)
            {
                SmartLineHeight = true
            };
            _text.LayoutChanged += new chTextLayout.LayoutChangedFunc(_text_LayoutChanged);
            AddListeners();
        }

        void _text_LayoutChanged()
        {
            if (!_stop_event)
                DoLayoutContent = true;
        }

        #endregion

        #region Layout

        protected override bool LayoutContent(cBox anchor)
        {
            _stop_event = true;
            var parent = Parent;

            if (HasWidth)
                _text.Width = VisContentWidth;
            else
                _text.RemoveWidth();
            _text.Layout();

            _stop_event = false;
            return true;
        }

        protected override void FlowContent(cBox anchor)
        {
            //Positions the text inside the box.
            //The basic problem is that text is PDF is draw downwards, while everything else is
            //drawn uppwards. This means the drawing point is at the bottom. The first line of
            //the text is still drawn above the drawing point, though.

            _text.OffsetX = VisBorderLeft + VisPaddingLeft;
            _text.OffsetY = VisBorderBottom + VisPaddingBottom
                //Must compensate for the fact that the first line is drawn above the position,
                //and all the next are drawn below the position.
                + (VisContentHeight - _text.GetLineHeight(0, 0));

            //switch (Style.TextAlignement)
            //{
            //    //We move the baseline up so that the text fit inside the box.
            //    case cTextAlignement.Descent:
            //        _text.OffsetY -= _text.LastLineDescent;
            //        break;

            //    case cTextAlignement.Middle:
            //        _text.OffsetY += (_text.FreeSpaceAbove + _text.LastLineDescent) / 2 - _text.LastLineDescent;
            //        break;
            //}
        }

        #endregion

        #region Render

        /// <summary>
        /// Renders the text box. State will be saved and restored.
        /// </summary>
        /// <param name="draw">Render target</param>
        /// <param name="state">Render state</param>
        public new void Render(IDraw draw, ref cRenderState state)
        {
            if (state.GS != GS.Page)
                throw new NotSupportedException("State must be page");

            draw.Save();
            state.Save();

            double move_x = Position.X, move_y = Position.Y;
            if (!PositionOver)
                move_y -= VisHeight;
            if (move_x != 0 || move_y != 0)
                state.PrependCTM(new xMatrix(move_x, move_y), draw);

            Render(draw, ref state, this);

            draw.Restore();
            state = state.Restore();
        }

        /// <summary>
        /// Renders just the text
        /// </summary>
        /// <remarks>
        /// Internal users should consider chTextLayout instead, as this class just wraps that.
        /// </remarks>
        public void RenderText(IDraw draw, cRenderState state, bool move_text)
        {
            _text.RenderText(draw, state, move_text);
        }

        protected override void DrawContent(IDraw draw, ref cRenderState state, cBox anchor)
        {
            _text.Render(draw, ref state);
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

        protected override void BlockLayoutChanged(cBox child)
        {
            
        }

        protected override void RemoveChildImpl(cNode child)
        {
            
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

        #endregion
    }
}
