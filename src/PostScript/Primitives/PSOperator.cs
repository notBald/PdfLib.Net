using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// Represents a executable operator
    /// </summary>
    public class PSOperator : PSItem
    {
        #region Variables and properties

        /// <summary>
        /// Name value
        /// </summary>
        public readonly string Operator;

        #endregion

        #region Init

        public PSOperator(string op) { Operator = op; }

        #endregion

        public override PSItem ShallowClone()
        {
            //Ignoring the Executable flag for PSOperators. Not sure if that's to the specs, mind.
            return this;
        }

        public override bool Equals(object obj)
        {
            throw new NotSupportedException();
        }

        public override int GetHashCode()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString()
        {
            return Operator;
        }
    }
}
