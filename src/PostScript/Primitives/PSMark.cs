using System.Diagnostics;
namespace PdfLib.PostScript.Primitives
{
    /// <summary>
    /// A mark is a special object used to denote a position on the operand stack. This
    /// use is described in the presentation of stack and array operators in Section 3.6,
    /// “Overview of Basic Operators.” There is only one value of type mark, created by
    /// invoking the operator mark, [, or <<. Mark objects are not legal operands for
    /// most operators. They are legal operands for ], >>, counttomark, cleartomark, and
    /// a few generic operators such as pop and type.
    /// </summary>
    [DebuggerDisplay("(PSMark)")]
    public sealed class PSMark : PSItem
    {
        public static readonly PSMark Instance = new PSMark();

        private PSMark() { }

        public override PSItem ShallowClone()
        {
            //Marks are not ever executable.
            return this;
        }
    }
}
