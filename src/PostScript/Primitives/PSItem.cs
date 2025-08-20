using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// Root class for all PostScript primitives
    /// </summary>
    /// <remarks>
    /// There are enough difference between PostScript and PDF
    /// primitives that I'm keeping them sepperate. Merging the
    /// two is certainly not impossible though.
    /// </remarks>
    public abstract class PSItem
    {
        /// <summary>
        /// Whenever this is an executable object or not.
        /// </summary>
        public bool Executable = false;

        public abstract PSItem ShallowClone();
    }
}
