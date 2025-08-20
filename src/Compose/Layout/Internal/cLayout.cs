using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using PdfLib.Pdf.Internal;
using PdfLib.Pdf.Primitives;
using PdfLib.Render;
using PdfLib.Pdf.ColorSpace;

namespace PdfLib.Compose.Layout
{
    //      About float, position and relative:
    //       - Float isn't really needed unless text support is added to containers.
    //         For now, sofisticated text layout will be handled by the old textbox.
    //       - For layout, position relative have no effect, but when rendering, relative
    //         is vissualy offset by left/right.
    //              - Is clipped to its parent container. IOW, rendring happens on the
    //                parent container.
    //       - For layout, position absolute has a big effect. The point the container would
    //         have been placed is calculated and stored on the div, but size is 0/0. During
    //         rendering, the container is added to the closest "position relative" block and
    //         offset left/right/top/bottom to that. (It's also clipped on this container)
    //          - Absolute rendering is done after the container has rendered.
    //
    //  Idea: The "layout stage" has two phases.
    //      - Phase 0: Build rough visual tree. No sizes are determined, just putting the structure together.
    //          - This can be done as children are added / removed.
    //              - Problems: 
    //                - No root - Solution:
    //                  - The only part of the structure that needs to be added to the root / anchor, needs to
    //                    be stored away until it gets added to said root.
    //                  - There will always only be one "parentless" div in a tree, so we know where to store
    //                    this data.
    //                - When a element is removed, absolute elements needs to be removed from the anchor parent
    //      - Phase 1: Containers that size can't be determined, has their size determined this way:
    //          - Size is set as if it was in an infinite container
    //          - Size is adjusted to % of its own size, this size will be used during Phase 2
    //      - Phase 2: Builds up a visual tree that will be used for rendering.
    //          - Creates displaylist for static/relative content
    //          - Creates displaylist for absolute content
    //          - Absolute content has its position determined, but has no "size"
    //          - Relative content is handled as it it wasn't relative
    //      Causes layout:
    //          - Changing between absolute and relative/static
    //          - Adjusting size
    //              - On absolute objects, only its contents is reflowed
    //          - Child is removed or added
    //          - I think it's best that all this instead sets a "dirty" flag. It's much preferable
    //            over hacking around exessive "layout" updates.
    //              - If a child is dirty, the root need to know. I.e. when a child sets itself dirty,
    //                the root is also marked dirty. No wait, instead, if a user calls "dolayout" a
    //                search is done for dirty flags. 
    //
    //  Render:
    //  - Position relative, with offset.
    //      - Offsets its static and absolute content
    //
    //
    //  Visual tree:
    //      - Divides children into lines. Each line has a width and a height.
    //      - Absolute elements is removed from its container and placed on the anchor element
    //          - Perhaps leave some sort of placeholder?
    //internal class cLayout
    //{
    //    /// <summary>
    //    /// Node that owns this layout object
    //    /// </summary>
    //    public readonly cBox Owner;


    //}

    /// <summary>
    /// Visual node
    /// </summary>
    //internal class cVNode
    //{
    //    /// <summary>
    //    /// Previous node in the list
    //    /// </summary>
    //    public cVNode Prev;

    //    /// <summary>
    //    /// Next node in the list
    //    /// </summary>
    //    public cVNode Next;

    //    /// <summary>
    //    /// A loopnode, useful for inserting and removing whole lists
    //    /// </summary>
    //    public cVNode Loop;
    //}
}
