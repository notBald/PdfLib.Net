using System;
using System.Collections.Generic;
using System.Diagnostics;
using PdfLib.Pdf.Primitives;
using PdfLib.Pdf.Filter;

namespace PdfLib.Pdf.Internal
{
    /// <summary>
    /// A XRef stream dictionary object
    /// </summary>
    [PdfVersion("1.5")]
    internal sealed class XRefStream : PdfTrailer
    {
        #region Variables and properties

        private readonly IWStream _stream;

        /// <summary>
        /// PdfType.XRefStream
        /// </summary>
        internal override PdfType Type { get { return PdfType.XRefStream; } }

        /// <summary>
        /// Array of subsections. Each pair of integer denote a subsection
        /// </summary>
        /// <remarks>
        /// Integer array, default [0 Size]
        /// </remarks>
        public int[] Index
        {
            get
            {
                var ret = ((IntArray)_elems.GetPdfType("Index", PdfType.IntArray));
                if (ret == null) return new int[] { 0, Size };//new IntArray(new PdfInt[] { new PdfInt(0), new PdfInt(Size) });
                return ret.ToArray();
            }
        }

        /// <summary>
        /// The size of each field in the cross reference stream.
        /// </summary>
        /// <remarks>
        /// There are always 3 integers in PDF 1.5.
        /// [1 4 2] gives 1 byte, 4 bytes, 2 bytes for instance
        /// Type 0: ObjectID and generation
        /// Type 1: Byteoffset and generation
        /// Type 2: ObjectID of object stream (with gen = 0) and index in this stream
        /// </remarks>
        public int[] W { get { return ((IntArray)_elems.GetPdfTypeEx("W", PdfType.IntArray)).ToArray(); } }

        #endregion

        #region Init

        internal XRefStream(IWStream stream)
            : base(stream.Elements)
        {
            _stream = stream;
            _elems.CheckTypeEx("XRef");
        }

        /// <summary>
        /// Initializes the XRefStream
        /// </summary>
        internal void Init(PdfFile Owner)
        {
            Debug.Assert(Owner.Header.IsAtMost(1, 7));
            Owner.SetXRefSpace(_elems.GetUInt("Size", 2));

            var w = W;
            var bytes = _stream.DecodedStream;
#if LONGPDF
            long[] nums = new long[w.Length];
#else
            int[] nums = new int[w.Length];
#endif
            var index = Index;

            //Sanity check.
            if (w.Length != 3) //Todo Error codes
                throw new PdfInternalException("Unfamiliar Stream XRef table");
            int size = 0;
            for (int c = 0; c < w.Length; c++)
                size += w[c];
            var idx = Index;
            if ((idx.Length & 1) != 0)
                throw new PdfInternalException("Corrupt Stream XRef table");
            int total_size_count = 0;
            for (int c = 1; c < idx.Length; c += 2)
                total_size_count += idx[c];
            if (size * total_size_count != bytes.Length)
                throw new PdfInternalException("Corrupt Stream XRef table");
            if (index.Length < 2 || (index.Length & 1) != 0)
                throw new PdfInternalException("Corrupt Stream XRef table");

            //Parses
            int count = 0; PdfObjID id;
            int id_next = 0, id_end = 0, index_pos = 0;
            while (count != bytes.Length)
            {
                //Translates bytes into numbers
                for (int c = 0; c < nums.Length; c++)
                {
                    int len = w[c];
                    if (len > 0)
                    {
                        int num = 0;
                        for (int i = 0; i < len; i++)
                        {
                            byte b = bytes[count++];
                            num = (num << 8) | b;
                        }
                        nums[c] = num;
                    }
                    else
                    {
                        //If the first element is zero, the type field shall 
                        //default to 1.
                        if (c == 0) nums[0] = 1;
                        //Other defaults are zero
                    }
                }

                if (id_next == id_end)
                {
                    id_next = index[index_pos++];
                    id_end = id_next + index[index_pos++];
                }

                switch (nums[0])
                {
                    case 0: //Linked list of free objects
                        //Ignore, PdfLib does not track free objects

                        break;

                    case 1: //ID (with 0 as generation) number and offset
                        Debug.Assert(id_next != 0);

                        id = new PdfObjID(id_next, (ushort)nums[2]);
                        if (!Owner.HasObjectLocation(id))
                            Owner.RegisterObject(id, nums[1]);

                        break;

                    case 2: //Compressed object
                        id = new PdfObjID(id_next, 0);
                        if (!Owner.HasObjectLocation(id))
                            Owner.RegisterObject(id, (int) nums[1], (int) nums[2]);
                        break;

                    //Other types are ignored
                }

                id_next++;
            }
        }

        #endregion

        #region Debug aid

        internal string HexStream 
        { 
            get 
            { 
                if (_stream is PdfStream)
                    return ((PdfStream)_stream).HexDump(W);
                return "Unknown";
            } 
        }

        #endregion
    }
}
