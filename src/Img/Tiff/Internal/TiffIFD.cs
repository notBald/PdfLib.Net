using System;
using System.Collections.Generic;
using System.Text;

namespace PdfLib.Img.Tiff.Internal
{
    public abstract class TiffIFD
    {
        protected SortedList<TagID, Tag> _tags;
        protected TiffStreamReader _file;
        protected bool _big;
        internal readonly uint IFD_SIZE;
        internal SortedList<TagID, Tag> Tags
        {
            set { if(_tags == null) _tags = value; }
            get { return _tags; }
        }

        /// <summary>
        /// If this image is big endian
        /// </summary>
        public bool BigEndian { get { return _file != null && _file.BigEndian; } }

        /// <summary>
        /// If this is a big tiff idf
        /// </summary>
        public bool BigTiff { get { return _big; } }

        protected TiffIFD(TiffStreamReader file, bool is_big_tiff)
        {
            _file = file;
            _big = is_big_tiff;
            IFD_SIZE = _big ? 8u : 4u;
        }

        internal int ReadFromFile(long file_offset, byte[] buff, int offset, int count)
        {
            if (file_offset == 0)
                throw new NullReferenceException("Tiff offset was zero");
            return _file.Read(file_offset, buff, offset, count);
        }

        /// <summary>
        /// Repairs common issues that can cause trouble for saving
        /// </summary>
        internal abstract void Repair();

        internal virtual void LoadIntoMemory()
        {
            var tags = new LinkedList<Tag>();
            foreach (var tag in _tags.Values)
            {
                var memtag = tag.LoadData();
                var meta = memtag.Meta;
                if (meta.IsOffsetData)
                    memtag = meta.CreateMemTag(memtag, _file, _big);
                if (!ReferenceEquals(tag, memtag))
                    tags.AddLast(memtag);
            }
            foreach (var tag in tags)
                _tags[tag.ID] = tag;
        }

        /// <summary>
        /// For byteswapping little endian data into big endian format, required for Pdf files.
        /// </summary>
        internal protected static byte[] ByteSwap(byte[] data, int w, int h, int bpc)
        {
            byte[] swapped = new byte[data.Length];
            switch (bpc)
            {
                case 16:
                    for (int a = 0, b = 1; b < data.Length; a += 2, b += 2)
                    {
                        swapped[a] = data[b];
                        swapped[b] = data[a];
                    }
                    break;
                case 24:
                    for (int a = 0, b = 1, c = 2; c < data.Length; a += 3, b += 3, c += 3)
                    {
                        swapped[a] = data[c];
                        swapped[b] = data[b];
                        swapped[c] = data[a];
                    }
                    break;
                case 32:
                    for (int a = 0, b = 1, c = 2, d = 3; d < data.Length; a += 4, b += 4, c += 4, d += 4)
                    {
                        swapped[a] = data[d];
                        swapped[b] = data[c];
                        swapped[c] = data[b];
                        swapped[d] = data[a];
                    }
                    break;
                case 64:
                    for (int a = 0, b = 1, c = 2, d = 3, e = 4, f = 5, g = 6, i = 7; i < data.Length; a += 8, b += 8, c += 8, d += 8, e += 8, f += 8, g += 8, i += 8)
                    {
                        swapped[a] = data[i];
                        swapped[b] = data[g];
                        swapped[c] = data[f];
                        swapped[d] = data[e];
                        swapped[e] = data[d];
                        swapped[f] = data[c];
                        swapped[g] = data[b];
                        swapped[i] = data[a];
                    }
                    break;
                default: throw new NotImplementedException("ByteSwap: " + bpc);
            }

            return swapped;
        }

        protected double? GetRational(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Type == DataType.RATIONAL && t.Count > 0)
                return t.GetRealValue();
            return null;
        }

        protected double[] GetRationals(TagID id)
        {
            var t = GetTag(id, DataType.RATIONAL);
            if (t == null) return null;
            return t.GetReals();
        }

        protected uint GetUIntEx(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t))
            {
                if (t.Type != DataType.LONG || t.Count != 1)
                    throw new TiffReadException("Expected single integer");

                return (uint)t.GetSingleValue();
            }
            throw new TiffReadException("Integer missing");
        }

        protected uint GetUInt(TagID id, uint default_value)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Type == DataType.LONG && t.Count > 0)
                return (uint) t.GetSingleValue();
            return default_value;
        }

        protected int GetIntEx(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t))
            {
                if (t.Type != DataType.LONG || t.Count != 1)
                    throw new TiffReadException("Expected single integer");
                return (int)t.GetSingleValue();
            }
            throw new TiffReadException("Integer missing");
        }

        protected int GetInt(TagID id, int default_value)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Type == DataType.LONG && t.Count > 0)
                return (int)t.GetSingleValue();
            return default_value;
        }

        protected ulong[] GetUIntsOrUShorts(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && (t.Type == DataType.LONG || t.Type == DataType.SHORT || t.Type == DataType.LONG8))
                return t.GetAsULongs();
            return null;
        }

        protected int GetIntOrUShortEx(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Count > 0)
            {
                if (t.Type == DataType.LONG)
                    return (int) t.GetSingleValue();
                if (t.Type == DataType.SHORT)
                    return (ushort)t.GetSingleValue();
                throw new TiffReadException("Expected integer");
            }
            throw new TiffReadException("Integer missing");
        }

        protected uint GetUIntOrUShort(TagID id, uint default_value)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Count > 0)
            {
                if (t.Type == DataType.LONG)
                    return (uint)t.GetSingleValue();
                if (t.Type == DataType.SHORT)
                    return (ushort)t.GetSingleValue();
            }
            return default_value;
        }

        protected int? GetIntOrUShort(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Count > 0)
            {
                if (t.Type == DataType.LONG)
                    return (int)t.GetSingleValue();
                if (t.Type == DataType.SHORT)
                    return (ushort)t.GetSingleValue();
            }
            return null;
        }

        protected Array GetIFDs(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t)
                && (t.Type == DataType.IFD || t.Type == DataType.IFD8 ||
                    t.Type == DataType.LONG || t.Type == DataType.LONG8))
            {
                if (t is MemTag)
                    return t.GetObjects();
                return t.GetAsULongs();
            }
            return null;
        }

        internal TiffIFD[] ParseIFDs(TagID id, IFDType type)
        {
            TiffIFD[] images;
            var idfs_ar = GetIFDs(id);
            if (idfs_ar == null) return null;
            images = new TiffIFD[idfs_ar.Length];
            if (idfs_ar is ulong[])
            {
                var idfs = (ulong[])idfs_ar;
                var file = _file;
                for (int c = 0; c < idfs.Length; c++)
                    images[c] = TiffStream.ParseTiff((long)idfs[c], _big, file, type);
            }
            else
            {
                var oa = (object[])idfs_ar;
                for (int c = 0; c < images.Length; c++)
                    images[c] = (TiffIFD) oa[c];
            }
            return images;
        }

        protected ushort GetUShortEx(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Count > 0)
            {
                if (t.Type != DataType.SHORT)
                    throw new TiffReadException("Expected integer");
                return (ushort)t.GetSingleValue();
            }
            throw new TiffReadException("Integer missing");
        }

        protected ushort GetUShort(TagID id, ushort default_value)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Type == DataType.SHORT && t.Count > 0)
                return (ushort)t.GetSingleValue();
            return default_value;
        }

        protected ushort? GetUShort(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Type == DataType.SHORT && t.Count > 0)
                return (ushort)t.GetSingleValue();
            return null;
        }

        protected string GetASCI(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Type == DataType.ASCII)
            {
                byte[] asci = t.GetBytes();

                if (asci.Length > 0)
                    return Encoding.ASCII.GetString(asci, 0, asci.Length - 1);
            }
            return null;
        }

        protected Tag GetTag(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t))
                return t;
            return null;
        }

        protected Tag GetTag(TagID id, DataType type)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Type == type)
                return t;
            return null;
        }

        protected Tag GetTag(TagID id, DataType type, int count)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && t.Type == type && (int) t.Count == count)
                return t;
            return null;
        }

        protected uint[] GetUInts(TagID id)
        {
            var t = GetTag(id, DataType.LONG);
            if (t == null || t.Count == 0) return null;
            return t.GetUInts();
        }

        protected ushort[] GetUShorts(TagID id)
        {
            var t = GetTag(id, DataType.SHORT);
            if (t == null || t.Count == 0) return null;
            return t.GetUShorts();
        }

        /// <summary>
        /// Fetches a tag
        /// </summary>
        /// <param name="id">The tag's id</param>
        /// <param name="count">Required count</param>
        /// <returns>Returns null if the count don't mach</returns>
        protected ushort[] GetUShorts(TagID id, int count)
        {
            var t = GetTag(id, DataType.SHORT);
            if (t == null || (int)t.Count != count) return null;
            return t.GetUShorts();
        }

        protected byte[] GetByteTag(TagID id)
        {
            Tag t;
            if (_tags.TryGetValue(id, out t) && (t.Type == DataType.BYTE || t.Type == DataType.SBYTE))
                return t.GetBytes();
            return null;
        }

        internal enum IFDType
        {
            Image = 254,
            Exif = 34665
        }
    }
}
