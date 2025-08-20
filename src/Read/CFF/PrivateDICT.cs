namespace PdfLib.Read.CFF
{
    class PrivateDICT : Parser
    {
        #region variables

        /// <summary>
        /// Offset to private subruties. (Offset from self)
        /// </summary>
        /// <remarks>
        /// The local subrs offset is relative to the beginning of the Private DICT data
        /// </remarks>
        public int? Subrs;

        #endregion

        #region Init

        public PrivateDICT(Util.StreamReaderEx r)
            : base(r, false) { }

        #endregion

        protected override void ParseCMD1()
        {
            if (_number == 19)
                Subrs = PopInt();
            Clear();
        }

        protected override void ParseCMD2()
        {
            Clear();
        }
    }
}
