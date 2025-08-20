using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// Objects are PS primitives with access modifier
    /// </summary>
    public abstract class PSObject : PSItem
    {
        /// <summary>
        /// Access rights for this object
        /// </summary>
        public virtual PSAccess Access { get; set; }

        public PSObject() { }
        public PSObject(PSObject o) { Access = o.Access; Executable = o.Executable; }

        /// <summary>
        /// Non dict objects needs to be shallowcloned. I've not looked
        /// too deeply into the specs on this issue though.
        /// </summary>
        public override abstract PSItem ShallowClone();

        internal abstract void Restore(PSItem obj);
    }

    public enum PSAccess
    {
        Unlimited,
        ReadOnly,
        ExecuteOnly,
        None
    }
}
