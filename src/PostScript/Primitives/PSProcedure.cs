using System.Collections.Generic;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// A procedure is a series of executable operators and operands
    /// </summary>
    /// <remarks>
    /// I assume that procedures are immutable (Make it work like PSArray if not)
    /// 
    /// Hmm, a closer look at the specs shows procedures to be normal arrays
    /// with the executable flag set.
    /// 
    /// Todo: Make this class inherit array
    /// </remarks>
    public sealed class PSProcedure : PSObject
    {
        #region Variables

        /// <summary>
        /// The items in the array
        /// </summary>
        public readonly PSItem[] Items;

        #endregion

        #region Init

        /// <summary>
        /// Create array from list
        /// </summary>
        public PSProcedure(List<PSItem> items)
        {
            Executable = true;
            Items = items.ToArray();
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        public PSProcedure(PSProcedure proc)
        {
            Executable = proc.Executable;
            Items = proc.Items;
        }

        #endregion

        public override PSItem ShallowClone()
        {
            return new PSProcedure(this);
        }

        internal override void Restore(PSItem obj)
        {
            Access = ((PSProcedure) obj).Access;
        }
    }
}
