using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.PostScript.Primitives;

namespace PdfLib.PostScript.Internal
{
    delegate void PSCB();

    /// <summary>
    /// Used to implement native PS functions
    /// </summary>
    [DebuggerDisplay("{Action.Method}")]
    internal class PSCallback : PSItem
    {
        #region Variables and properties

        /// <summary>
        /// Command to execute when invoked
        /// </summary>
        public readonly PSCB Action;

        #endregion

        #region Init

        public PSCallback(PSCB action) { Action = action; Executable = true; }
        public PSCallback(PSCallback c) { Action = c.Action; Executable = c.Executable; }

        #endregion

        public override PSItem ShallowClone()
        {
            return new PSCallback(this);
        }
    }
}
