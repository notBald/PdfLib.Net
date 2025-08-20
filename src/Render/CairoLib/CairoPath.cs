using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Render.CairoLib
{
    public class CairoPath
    {
        /// <summary>
        /// Transform to set before drawing the path
        /// </summary>
        public cMatrix? Transform;

        public readonly cPath Path;

        public cPathData[] Points { get { return Path.Data.Points; } }

        public CairoPath(cPath path)
        { Path = path; }
    }
}
