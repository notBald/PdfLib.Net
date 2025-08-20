using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfLib.Util;
using PdfLib.Img.Tiff.Internal;
using System.Diagnostics;


namespace PdfLib.Img.Tiff
{
    public class TiffWriter : IDisposable
    {
        bool _big_tiff, _big_endian, _dispose_file;
        long _pos;
        Stream _output;
        byte[] _buffer, _ifd;
        int _buffer_pos, _ifd_pos;
        internal bool BigEndian { get { return _big_endian; } }
        internal bool BigTiff { get { return _big_tiff; } }
        internal readonly uint IFD_SIZE;
        internal long Position { get { return _pos; } }

#if DEBUG
        /// <summary>
        /// Only avalible in debug mode
        /// </summary>
        public SortedList<TagID, Tag> CurrentTags
        {
            get
            {
                if (_ifd == null || _ifd_pos < 12) return null;
                if (_big_tiff)
                    return TiffStream.CreateTags((int)
                        StreamReaderEx.ReadULong(_big_endian, 0, _ifd),
                        _ifd, 8, new TiffImage(new TiffStreamReader(null, _big_endian), true));
                else
                    return TiffStream.CreateTags(
                        StreamReaderEx.ReadUShort(_big_endian, 0, _ifd),
                        _ifd, 2, new TiffImage(new TiffStreamReader(null, _big_endian), false));
            }
        }
#endif

        #region Init and dispose

        private TiffWriter(Stream output, bool big_tiff, bool be, bool df)
        {
            if (!output.CanWrite)
                throw new ArgumentException("Need writable stream");

            _output = output; 
            _big_tiff = big_tiff; 
            _big_endian = be; 
            _dispose_file = df;
            IFD_SIZE = big_tiff ? 8u : 4u;

            //Big enough for one tag
            _buffer = new byte[20];
        }
        ~TiffWriter() { DisposeImpl(); }

        public static TiffWriter Open(string file)
        {
            return Open(File.Create(file), false, false, true);
        }

        public static TiffWriter Open(Stream file, bool big_endian, bool big_tiff, bool close_file)
        {
            var tw = new TiffWriter(file, big_tiff, big_endian, close_file);
            tw.WriteHeader();
            return tw;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            EndFile();
            DisposeImpl();
        }

        private void DisposeImpl()
        {
            if (_dispose_file)
            {
                _output.Dispose();
                _dispose_file = false;
            }
        }

        #endregion

        #region Header

        private void WriteHeader()
        {
            if (_big_endian)
                _buffer[0] = _buffer[1] = 0x4D;
            else
                _buffer[0] = _buffer[1] = 0x49;

            if (_big_tiff)
            {
                BWriter.Write(_big_endian, 43, 2, _buffer);
                BWriter.Write(_big_endian, 8, 4, _buffer);
                BWriter.Write(_big_endian, 0, 6, _buffer);
                _output.Write(_buffer, 0, 8);
                _pos = 8;
            }
            else
            {
                BWriter.Write(_big_endian, 42, 2, _buffer);
                _output.Write(_buffer, 0, 4);
                _pos = 4;
            }
        }

        #endregion

        public void Write(TiffImage image)
        {
            if (_pos > 8)
            {
                //Modify ifd to have the multi image flag set
                AddTag();

                Debug.Assert(_pos % 2 == 0);
                Write(_ifd, 0, _ifd_pos);
            }

            image.Repair();
            var next_avalible_pos = (ulong)_pos;
            next_avalible_pos += _big_tiff ? 8u : 4u;
            var tags = CreateTagList(image.Tags, image, next_avalible_pos);
            next_avalible_pos += tags.Size;
            if (next_avalible_pos % 2 != 0)
                next_avalible_pos++;

            //Writes the position of the image format descriptor
            WritePos(next_avalible_pos);

            _ifd_pos = Write(tags.List, ref _ifd);
        }

        /// <summary>
        /// Adds the NewSubfileType to the _idf
        /// </summary>
        private void AddTag()
        {
            //Finds the tag, if it's in the ifd
            int pos;
            if (_big_tiff)
                pos = FindTag(TagID.NewSubfileType, 8, 20, _ifd);
            else
                pos = FindTag(TagID.NewSubfileType, 2, 12, _ifd);

            if (pos == -1)
            {
                //Tag was not in the ifd, so we add it
                if (_big_tiff)
                {
                    AddTag(TagID.NewSubfileType, DataType.LONG, 1, 2, 8, 20, ref _ifd);
                    _ifd_pos += 20;
                }
                else
                {
                    AddTag(TagID.NewSubfileType, DataType.LONG, 1, 2, 2, 12, ref _ifd);
                    _ifd_pos += 12;
                }
            }
            else
            {
                //Tag already in the ifd, so we modify it
                var be = BigEndian;
                if (_big_tiff)
                {
                    pos += 12;
                    BWriter.Write(be, Util.StreamReaderEx.ReadULong(be, pos, _ifd) | 2, pos, _ifd);
                }
                else
                {
                    pos += 8;
                    BWriter.Write(be, Util.StreamReaderEx.ReadUInt(be, pos, _ifd) | 2, pos, _ifd);
                }
            }
        }

        /// <summary>
        /// Adds a tag to a ifd, note, assumes the tag is not already there and can
        /// only write 4 byte data types.
        /// </summary>
        private void AddTag(TagID tag, DataType type, uint count, 
            uint val, int start, int advance, ref byte[] ifd)
        {
            Array.Resize<byte>(ref ifd, ifd.Length + advance);
            var be = BigEndian;

            //Increment the count
            if (start == 8)
                BWriter.Write(be, Util.StreamReaderEx.ReadULong(be, 0, ifd) + 1, 0, ifd);
            else
                BWriter.Write(be, (ushort)(Util.StreamReaderEx.ReadUShort(be, 0, ifd) + 1), 0, ifd);

            //Find the first tag after the one we want to add
            while (start + 2 < ifd.Length)
            {
                var a_tag = (TagID)Util.StreamReaderEx.ReadUShort(be, start, ifd);
                if (a_tag > tag) break;
                start += advance;
            }

            //Move all tags down, unless we're adding at the bottom
            if (start + 2 < ifd.Length)
                Buffer.BlockCopy(ifd, start, ifd, start + advance, ifd.Length - start - advance);
            else
                start = ifd.Length - advance;

            //Write the tag
            BWriter.Write(be, (ushort)tag, start, ifd); start += 2;
            BWriter.Write(be, (ushort)type, start, ifd); start += 2;
            if (advance == 20)
            {
                BWriter.Write(be, (ulong)count, start, ifd); start += 8;
                BWriter.Write(be, (uint)val, start, ifd);
            }
            else
            {
                BWriter.Write(be, (uint)count, start, ifd); start += 4;
                BWriter.Write(be, (uint)val, start, ifd);
            }
        }

        private int FindTag(TagID tag, int start, int advance, byte[] ifd)
        {
            var be = BigEndian;
            while (start + 2 < ifd.Length)
            {
                var a_tag = (TagID) Util.StreamReaderEx.ReadUShort(be, start, ifd);
                if (a_tag == tag)
                    return start;
                if (a_tag > tag) break;
                start += advance;
            }
            return -1;
        }

        /// <summary>
        /// Writes out all but the ifd data
        /// </summary>
        /// <param name="tags">Tags to write out to output</param>
        /// <param name="ifd">fills out this array with the image field data</param>
        /// <returns>Number of bytes written into ifd</returns>
        internal int Write(List<KeyValuePair<Tag, Tag.MetaTag>> tags, ref byte[] ifd)
        {
            int ifd_pos;

            if (_big_tiff)
            {
                //Buffer for idf, will be written last
                int idf_size = tags.Count * 20 + 8;
                if (ifd == null || ifd.Length < idf_size)
                    ifd = new byte[idf_size];
                BWriter.Write(_big_endian, (ulong)tags.Count, 0, ifd);
                ifd_pos = 8;

                foreach (KeyValuePair<Tag, Tag.MetaTag> kv in tags)
                {
                    var tag = kv.Key;
                    var meta = kv.Value;

                    BWriter.Write(_big_endian, (ushort)tag.ID, ifd_pos, ifd); ifd_pos += 2;
                    BWriter.Write(_big_endian, (ushort)tag.Type, ifd_pos, ifd); ifd_pos += 2;
                    BWriter.Write(_big_endian, (ulong)tag.Count, ifd_pos, ifd); ifd_pos += 8;

                    if (meta.IsOffsetData)
                    {
                        var offset = _pos;
                        var offsets = new uint[tag.Count];
                        var counts = meta.Write(this);
                        WriteOffset(counts.Key, counts.Value, ifd, ifd_pos);

                        //Must update the data type
                        BWriter.Write(_big_endian, (ushort)counts.Key, ifd_pos - 10, ifd);
                    }
                    else
                    {
                        int tag_size = tag.TagSize;
                        if (tag.Count == 1 && tag_size <= 4)
                        {
                            if (tag_size == 8)
                                BWriter.Write(_big_endian, (ulong)tag.GetSingleValue(), ifd_pos, ifd);
                            else if (tag_size == 4)
                                BWriter.Write(_big_endian, (uint)tag.GetSingleValue(), ifd_pos, ifd);
                            else if (tag_size == 2)
                                BWriter.Write(_big_endian, (ushort)tag.GetSingleValue(), ifd_pos, ifd);
                            else
                                ifd[ifd_pos] = (byte)tag.GetSingleValue();
                        }
                        else
                            WriteOffset(tag.Type, tag.GetAsULongs(), ifd, ifd_pos);
                    }

                    ifd_pos += 8;
                }
            }
            else
            {
                //Buffer for ifd, will be written last
                int idf_size = tags.Count * 12 + 2;
                if (ifd == null || ifd.Length < idf_size)
                    ifd = new byte[idf_size];
                BWriter.Write(_big_endian, (ushort)tags.Count, 0, ifd);
                ifd_pos = 2;

                foreach (KeyValuePair<Tag, Tag.MetaTag> kv in tags)
                {
                    var tag = kv.Key;
                    var meta = kv.Value;

                    BWriter.Write(_big_endian, (ushort)tag.ID, ifd_pos, ifd); ifd_pos += 2;
                    //Should I verify that the type is valid (meta.DTypes)?
                    BWriter.Write(_big_endian, (ushort)tag.Type, ifd_pos, ifd); ifd_pos += 2;
                    BWriter.Write(_big_endian, (uint)tag.Count, ifd_pos, ifd); ifd_pos += 4;

                    if (meta.IsOffsetData)
                    {
                        var offset = _pos;
                        var offsets = new uint[tag.Count];
                        var counts = meta.Write(this);
                        WriteOffset(counts.Key, counts.Value, ifd, ifd_pos);

                        //Must update the data type
                        BWriter.Write(_big_endian, (ushort)counts.Key, ifd_pos - 6, ifd);
                    }
                    else
                    {
                        int tag_size = tag.TagSize;
                        if (tag.Count == 1 && tag_size <= 4)
                        {
                            if (tag_size == 4)
                                BWriter.Write(_big_endian, (uint)tag.GetSingleValue(), ifd_pos, ifd);
                            else if (tag_size == 2)
                                BWriter.Write(_big_endian, (ushort)tag.GetSingleValue(), ifd_pos, ifd);
                            else
                                ifd[ifd_pos] = (byte)tag.GetSingleValue();
                        }
                        else
                        {
                            if (tag_size == 1)
                                WriteOffset(tag.Type, tag.GetBytes(), ifd, ifd_pos);
                            else
                                WriteOffset(tag.Type, tag.GetAsULongs(), ifd, ifd_pos);
                        }
                    }

                    ifd_pos += 4;
                }
            }

            if (_pos % 2 != 0)
                WriteByte(0);
            return ifd_pos;
        }

        private void WriteOffset(DataType type, byte[] data, byte[] ifd, int ifd_pos)
        {           
            if (data.Length <= IFD_SIZE)
                Array.Copy(data, 0, ifd, ifd_pos, data.Length);
            else
            {
                if (_pos % 2 != 0)
                    WriteByte(0);

                if (_big_tiff)
                    BWriter.Write(_big_endian, (ulong)_pos, ifd_pos, ifd);
                else
                    BWriter.Write(_big_endian, (uint)_pos, ifd_pos, ifd);
                Write(data, 0, data.Length);
            }
        }

        private void WriteOffset(DataType type, ulong[] data, byte[] ifd, int ifd_pos)
        {
            byte[] ba;
            int ba_pos = 0;
            switch (Tag.GetTagSize(type))
            {
                case 1:
                    ba_pos = data.Length;
                    ba = new byte[ba_pos];
                    for (int c = 0; c < data.Length; c++)
                        ba[c] = (byte) data[c];
                    break;
                case 2:
                    ba = new byte[data.Length * 2];
                    for (int c = 0; c < data.Length; c++)
                    {
                        BWriter.Write(_big_endian, (ushort)data[c], ba_pos, ba);
                        ba_pos += 2;
                    }
                    break;
                case 4:
                    ba = new byte[data.Length * 4];
                    for (int c = 0; c < data.Length; c++)
                    {
                        BWriter.Write(_big_endian, (uint)data[c], ba_pos, ba);
                        ba_pos += 4;
                    }
                    break;
                case 8:
                    ba = new byte[data.Length * 8];
                    for (int c = 0; c < data.Length; c++)
                    {
                        BWriter.Write(_big_endian, (ulong)data[c], ba_pos, ba);
                        ba_pos += 8;
                    }
                    break;
                default: throw new NotSupportedException();
            }
            if (ba_pos <= IFD_SIZE)
                Array.Copy(ba, 0, ifd, ifd_pos, ba_pos);
            else
            {
                if (_pos % 2 != 0)
                    WriteByte(0);

                if (_big_tiff)
                    BWriter.Write(_big_endian, (ulong)_pos, ifd_pos, ifd);
                else
                    BWriter.Write(_big_endian, (uint)_pos, ifd_pos, ifd);
                Write(ba, 0, ba_pos);
            }
        }

        /// <summary>
        /// Creates a list of tags to write out, and estimates the size of the non-ifd data
        /// </summary>
        internal TagList CreateTagList(TiffIFD ifd, ulong pos)
        {
            return CreateTagList(ifd.Tags, ifd, pos);
        }

        /// <summary>
        /// Creates a list of tags to write out, and estimates the size of the non-ifd data
        /// </summary>
        private TagList CreateTagList(SortedList<TagID, Tag> tags, TiffIFD ifd, ulong pos)
        {
            var ll = new List<KeyValuePair<Tag, Tag.MetaTag>>(tags.Count);
            ulong total_size = 0;
            foreach (KeyValuePair<TagID, Tag> kv in tags)
            {
                var tag = kv.Value;
                var meta = tag.Meta;
                if (meta.WriteTag)
                {
                    var tag_size = meta.Size(this, ifd, tag, pos + total_size);
                    if (ifd is TiffImage)
                    {
                        //Removes tags with default values.
                        var def_val = meta.Default((TiffImage)ifd);
                        if (def_val != null)
                        {
                            if (def_val is uint)
                            {
                                //Removes single value defaults.
                                if (tag_size == 0)
                                {
                                    if ((uint)def_val == tag.GetSingleValue())
                                        continue;
                                }
                            }
                            else if (def_val is ushort[])
                            {
                                var vals = tag.GetAsUShorts();
                                if (Util.ArrayHelper.ArraysEqual<ushort>(vals, (ushort[])def_val))
                                    continue;
                            }
                            else
                            {
                                //Todo: remove multivalue defaults
                            }
                        }
                    }
                    if (tag_size > 0)
                    {
                        //Skips tags that point at nothing
                        if (!tag.HasData)
                            continue;

                        total_size += tag_size;
                    }
                    ll.Add(new KeyValuePair<Tag, Tag.MetaTag>(tag, meta));

                    var replace_id = meta.Replace;
                    if (replace_id != TagID.PROGRESSIVE)
                    {
                        for (int c = ll.Count - 1; c >= 0; c--)
                        {
                            var llk = ll[c];
                            if (llk.Key.ID == replace_id)
                            {
                                if ((ulong)llk.Key.TagSize * llk.Key.Count > IFD_SIZE)
                                    throw new TiffNotImplementedException("Recalcing size");
                                ll[c] = llk = new KeyValuePair<Tag,Tag.MetaTag>(meta.CreateAssocTag(replace_id, tag), llk.Value);
                                if ((ulong)llk.Key.TagSize * llk.Key.Count > IFD_SIZE)
                                    throw new TiffNotImplementedException("Recalcing size");
                                break;
                            }
                        }
                    }
                }
                else if (meta.Replace != TagID.PROGRESSIVE)
                {
                    var replace_id = meta.Replace;
                    if (replace_id == tag.ID)
                    {
                        //Tag can do the replacement itself
                        tag = meta.CreateAssocTag(replace_id, tag);
                        if (tag != null)
                        {
                            ll.Add(new KeyValuePair<Tag, Tag.MetaTag>(tag, meta));
                            total_size += meta.Size(this, ifd, tag, pos + total_size);
                        }
                    }
                    else
                    {
                        //Search for a tag that will do the replacement
                        for (int c = ll.Count - 1; c >= 0; c--)
                        {
                            var llk = ll[c];
                            if (llk.Key.ID == replace_id)
                            {
                                tag = llk.Value.CreateAssocTag(tag.ID, tag);
                                ll.Add(new KeyValuePair<Tag, Tag.MetaTag>(tag, meta));
                                total_size += meta.Size(this, ifd, tag, pos + total_size);
                                break;
                            }
                        }
                    }
                }
            }

            return new TagList(total_size, ll);
        }

        internal void WritePos(ulong pos)
        {
            if (_big_tiff)
                Write((ulong)pos);
            else
                Write((uint)pos);
            Flush();
        }

        internal void WriteByte(byte b)
        {
            _output.WriteByte(b);
            _pos++;
        }

        internal void Write(byte[] bytes, int offset, int count)
        {
            _output.Write(bytes, offset, count);
            _pos += count;
        }

        internal void Write(ulong value)
        {
            BWriter.Write(_big_endian, value, 0, _buffer);
            _buffer_pos += 8;
        }

        internal void Write(uint value)
        {
            BWriter.Write(_big_endian, value, 0, _buffer);
            _buffer_pos += 4;
        }

        internal void Flush()
        {
            _output.Write(_buffer, 0, _buffer_pos);
            _pos += _buffer_pos;
            _buffer_pos = 0;
        }

        internal void EndFile()
        {
            if (_pos != -1 && _ifd != null)
            {
                Write(_ifd, 0, _ifd_pos);

                if (_big_tiff)
                    Write(0ul);
                else
                    Write(0u);
                Flush();
                _pos = -1;
            }
        }

        internal struct TagList
        {
            public ulong Size;
            public List<KeyValuePair<Tag, Tag.MetaTag>> List;
            public TagList(ulong s, List<KeyValuePair<Tag, Tag.MetaTag>> l)
            { Size = s; List = l; }
        }
    }

    /// <summary>
    /// Byte writer
    /// </summary>
    internal static class BWriter
    {
        #region Write to buffer
        //If there's a need to make these public/internal, put them in another file.

        internal static void Write(bool big_endian, ulong value, int pos, byte[] buf)
        {
            if (big_endian) WriteBE(value, pos, buf); else WriteLE(value, pos, buf);
        }

        /// <summary>
        /// Converts a value to a big endian byte array
        /// </summary>
        /// <returns>Big endian bytes</returns>
        private static void WriteBE(ulong value, int pos, byte[] ba)
        {
            ba[pos + 0] = (byte)((value >> 56) & 0xFF);
            ba[pos + 1] = (byte)((value >> 48) & 0xFF);
            ba[pos + 2] = (byte)((value >> 40) & 0xFF);
            ba[pos + 3] = (byte)((value >> 32) & 0xFF);
            ba[pos + 4] = (byte)((value >> 24) & 0xFF);
            ba[pos + 5] = (byte)((value >> 16) & 0xFF);
            ba[pos + 6] = (byte)((value >> 8) & 0xFF);
            ba[pos + 7] = (byte)(value & 0xFF);
        }

        /// <summary>
        /// Converts a value to a little endian byte array
        /// </summary>
        /// <returns>Little endian bytes</returns>
        private static void WriteLE(ulong value, int pos, byte[] ba)
        {
            ba[pos + 7] = (byte)((value >> 56) & 0xFF);
            ba[pos + 6] = (byte)((value >> 48) & 0xFF);
            ba[pos + 5] = (byte)((value >> 40) & 0xFF);
            ba[pos + 4] = (byte)((value >> 32) & 0xFF);
            ba[pos + 3] = (byte)((value >> 24) & 0xFF);
            ba[pos + 2] = (byte)((value >> 16) & 0xFF);
            ba[pos + 1] = (byte)((value >> 8) & 0xFF);
            ba[pos + 0] = (byte)(value & 0xFF);
        }

        internal static void Write(bool big_endian, uint value, int pos, byte[] buf)
        {
            if (big_endian) WriteBE(value, pos, buf); else WriteLE(value, pos, buf);
        }

        /// <summary>
        /// Converts a value to a big endian byte array
        /// </summary>
        /// <returns>Big endian bytes</returns>
        internal static void WriteBE(uint value, int pos, byte[] ba)
        {
            ba[pos + 0] = (byte)((value >> 24) & 0xFF);
            ba[pos + 1] = (byte)((value >> 16) & 0xFF);
            ba[pos + 2] = (byte)((value >> 8) & 0xFF);
            ba[pos + 3] = (byte)(value & 0xFF);
        }

        /// <summary>
        /// Converts a value to a little endian byte array
        /// </summary>
        /// <returns>Little endian bytes</returns>
        internal static void WriteLE(uint value, int pos, byte[] ba)
        {
            ba[pos + 3] = (byte)((value >> 24) & 0xFF);
            ba[pos + 2] = (byte)((value >> 16) & 0xFF);
            ba[pos + 1] = (byte)((value >> 8) & 0xFF);
            ba[pos + 0] = (byte)(value & 0xFF);
        }

        internal static void Write(bool big_endian, ushort value, int pos, byte[] buf)
        {
            if (big_endian) WriteBE(value, pos, buf); else WriteLE(value, pos, buf);
        }

        /// <summary>
        /// Converts a value to a big endian byte array
        /// </summary>
        /// <returns>Big endian bytes</returns>
        private static void WriteBE(ushort value, int pos, byte[] ba)
        {
            ba[pos + 0] = (byte)((value >> 8) & 0xFF);
            ba[pos + 1] = (byte)(value & 0xFF);
        }

        /// <summary>
        /// Converts a value to a little endian byte array
        /// </summary>
        /// <returns>Little endian bytes</returns>
        private static void WriteLE(ushort value, int pos, byte[] ba)
        {
            ba[pos + 1] = (byte)((value >> 8) & 0xFF);
            ba[pos + 0] = (byte)(value & 0xFF);
        }

        #endregion
    }
}
