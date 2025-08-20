using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Compose.Layout
{
    public enum eBorderCollapse
    {
        collapse,
        separate
    }

    public enum eBoxSizing
    {
        BorderBox,
        PaddingBox,
        ContentBox
    }

    /// <summary>
    /// Inline elements:
    ///    1. respect left & right margins and padding, but not top & bottom
    ///    2. cannot have a width and height set
    ///    3. allow other elements to sit to their left and right.
    ///    4. see very important side notes on this here.

    /// Block elements:
    ///    1. respect all of those
    ///    2. force a line break after the block element
    ///    3. acquires full-width if width not defined

    //Inline-block elements:
    //    1. allow other elements to sit to their left and right
    //    2. respect top & bottom margins and padding
    //    3. respect height and width
    /// </summary>
    public enum eDisplay
    {
        /// <summary>
        /// Box will take up no space and not be displayed
        /// </summary>
        None = -1,

        /// <summary>
        /// The box will take up the full width
        /// </summary>
        Block,

        /// <summary>
        /// The box will be put on the same line as other inline blocks, if there is room.
        /// </summary>
        /// <remarks>
        /// Function's like html's inline-block. If you want just inline, then
        /// don't set width/height nor top/bottom padding. I don't see the need
        /// to outright support inline. If you're setting these values, you probably
        /// want inline-block anyway. 
        /// </remarks>
        Inline
    }

    /// <summary>
    /// Horizontal position in the box
    /// </summary>
    public enum eHorizontalPos
    {
        Left,
        Center,
        Right,
        Column
    }

    /// <summary>
    /// Vertical position on a single line
    /// </summary>
    public enum eLinePos
    {
        Top,
        Middle,
        Bottom
    }

    public enum ePosition
    {
        Static = 0,
        Relative,
        Absolute
    }

    public enum eTableLayout
    {
        Auto,
        Fixed
    }

    public enum eWhiteSpace
    {
        /// <summary>
        /// Whitespace is respected. Text will only wrap on line breaks.
        /// </summary>
        pre,

        /// <summary>
        /// Whitespace is preserved by the browser. Text will wrap when necessary, and on line breaks
        /// </summary>
        pre_wrap
    }

    /// <summary>
    /// How the content is positioned within a cText box.
    /// </summary>
    public enum eTextAlignement
    {
        /// <summary>
        /// Text is aligned on the last descend value
        /// </summary>
        Descent,

        /// <summary>
        /// Text is aligned on the baseline
        /// </summary>
        Baseline,

        /// <summary>
        /// Text is centered, so that there's an equal amount of free space above and below. 
        /// </summary>
        Middle,

        /// <summary>
        /// Text is centered, descent is ignored.
        /// </summary>
        Center
    }

    /// <summary>
    /// Vertical position in the box
    /// </summary>
    public enum eVerticalPos
    {
        /// <summary>
        /// Children will be placed at the top
        /// </summary>
        Top,

        /// <summary>
        /// Children will be placed so that there's equal space above and below
        /// </summary>
        Center,

        /// <summary>
        /// Children will placed at the bottom
        /// </summary>
        Bottom,

        /// <summary>
        /// Items will be streached out so that the free space is spread evenly between them
        /// </summary>
        Even
    }
}
