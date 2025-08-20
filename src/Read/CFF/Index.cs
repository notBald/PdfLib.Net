using System;

namespace PdfLib.Read.CFF
{
    /// <summary>
    /// An Index is essentially an array. 
    /// </summary>
    struct Index
    {
        /// <summary>
        /// Number of objects stored in this index
        /// </summary>
        public ushort count;

        /// <summary>
        /// The size of all the offsets
        /// </summary>
        public byte offSize;

        /// <summary>
        /// Offset into the file for retriving object data
        /// </summary>
        /// <remarks>This is an absolute address</remarks>
        public int offset;

        /// <summary>
        /// The absolute end of the index
        /// </summary>
        public int end;

        /// <summary>
        /// Gets all the bytes of an object in the index
        /// </summary>
        public byte[] GetBytes(int index, Util.StreamReaderEx s)
        {
            if (index >= count) throw new IndexOutOfRangeException();

            //Moves to start the offset.
            s.Position = this.offset + index * offSize;

            int offset = ReadOffset(offSize, s);
            int length = ReadOffset(offSize, s) - offset;

            //Moves to the start of the data.
            s.Position = this.offset + offSize * (count + 1) + offset - 1;

            //Reads out the data;
            byte[] ret = new byte[length];
            s.Read(ret, 0, length);

            return ret;
        }

        /// <summary>
        /// Gets the size and position of an index.
        /// </summary>
        internal SizePos GetSizePos(int index, Util.StreamReaderEx s)
        {
            if (index >= count)
                throw new IndexOutOfRangeException();

            //Moves to start the offset.
            s.Position = this.offset + index * offSize;

            int offset = ReadOffset(offSize, s);
            int length = ReadOffset(offSize, s) - offset;

            //Must do (count + 1) since there's always an extra offset
            // Todo: Why do I need -1?
            return new SizePos { length = length, start = offset + this.offset + offSize * (count + 1) - 1 };
        }

        public int GetLength(int index, Util.StreamReaderEx s)
        {
            if (index >= count) throw new IndexOutOfRangeException();

            //Moves to start the offset.
            s.Position = offset + index * offSize; ;

            //Finds the length by reading one offset and
            //subtracting it from the next.
            int ret = ReadOffset(offSize, s);
            ret = ReadOffset(offSize, s) - ret;

            return ret;
        }

        /// <summary>
        /// Position from where data begins.
        /// </summary>
        public int Start
        {
            get
            {
                return offset + offSize * count;
            }
        }

        internal static int ReadOffset(int size, Util.StreamReaderEx s)
        {
            if (size == 4) return s.ReadInt();
            else if (size == 2) return s.ReadUShort();
            else if (size == 1) return s.ReadByte();
            if (size != 3) throw new IndexOutOfRangeException();
            int ret = 0;
            for (int c = 0; c < 3; c++)
                ret = (ret << 8) | s.ReadByte();
            return ret;
        }
    }
}
