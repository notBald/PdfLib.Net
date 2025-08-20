using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PdfLib.PostScript.Primitives;

namespace PdfLib.PostScript.Internal
{
    /// <summary>
    /// Save object
    /// </summary>
    internal class PSSave : PSObject
    {
        #region Variables and properties

        private object _save_data;

        #endregion

        #region Init

        public PSSave(object save)
        {
            _save_data = save;
        }

        #endregion

        public override PSItem ShallowClone()
        {
            throw new NotImplementedException("Save state duplication");
        }

        internal override void Restore(PSItem obj)
        {
            throw new NotImplementedException();
        }
    }
}
