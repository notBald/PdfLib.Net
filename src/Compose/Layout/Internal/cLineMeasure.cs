using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Compose.Layout.Internal
{
    internal class cLineMeasure
    {
        /// <summary>
        /// Total width and Height of the line
        /// </summary>
        public readonly double Width, Height;

        /// <summary>
        /// The smallest size of the largest block on the line
        /// </summary>
        public readonly double MinSingleBlockWidth;

        /// <summary>
        /// Index of the last block measured
        /// </summary>
        public readonly int LastBlockIndex;
        public readonly cNode LastBlock;

        /// <summary>
        /// If any blocks on this line needs the size of the parent
        /// </summary>
        public readonly bool NeedParentSize;

        internal cLineMeasure() { }

        internal cLineMeasure(cNode last_block, double width, int last_ch_idx, double height, bool dep_on_parent, double max_width)
        {
            LastBlock = last_block;
            Width = width;
            LastBlockIndex = last_ch_idx;
            Height = height;
            NeedParentSize = dep_on_parent;
            MinSingleBlockWidth = max_width;
        }
    }
}
