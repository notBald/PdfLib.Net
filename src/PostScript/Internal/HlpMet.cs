using PdfLib.PostScript.Primitives;

namespace PdfLib.PostScript.Internal
{
    /// <summary>
    /// Helper methods. 
    /// </summary>
    public static class HlpMet
    {
        #region Creators

        /// <summary>
        /// Unpack PSItems into numbers
        /// </summary>
        public static double DblCreate(PSItem itm)
        {
            if (itm is PSInt)
                return ((PSInt)itm).Value;
            if (itm is PSReal)
                return ((PSReal)itm).Value;
            throw new PSCastException();
        }

        /// <summary>
        /// Put numbers into PSItems
        /// </summary>
        public static PSItem DblToItem(double num)
        {
            var inum = (int)num;
            if (num == inum)
                return new PSInt(inum);
            return new PSReal(num);
        }

        /// <summary>
        /// Method for creating a value array for doubles
        /// </summary>
        public static PSValArray<double, PSItem> CrDblArray(PSArray ar)
        {
            return new PSValArray<double, PSItem>(ar, DblToItem, DblCreate);
        }

        #endregion
    }
}
